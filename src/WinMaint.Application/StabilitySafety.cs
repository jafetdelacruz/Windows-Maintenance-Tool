using System.Diagnostics;
using WinMaint.Domain;

namespace WinMaint.Application;

public sealed record StabilityRuntimeSample(long? AvailableMemoryBytes, bool? SufficientPower, bool? ReliableThermalCritical);

public interface IStabilitySafetySource
{
    Task<DiagnosticReport> RefreshAsync(CancellationToken cancellationToken);
    Task<StabilityRuntimeSample> SampleAsync(CancellationToken cancellationToken);
}

public static class StabilityRuntimePolicy
{
    public static string? StopReason(StabilityRuntimeSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (sample.ReliableThermalCritical == true) return "Telemetría fiable señala temperatura crítica.";
        if (sample.AvailableMemoryBytes is null or < 268_435_456) return "RAM disponible insuficiente o no verificable.";
        if (sample.SufficientPower != true) return "Energía insuficiente o no verificable.";
        return null;
    }
}

internal static class GuardedStabilityExecution
{
    internal static async Task<StabilityProbeResult> RunAsync(
        IStabilityProbe probe, StabilityProbeContext context, IStabilitySafetySource? source,
        TimeSpan interval, CancellationToken token)
    {
        var elapsed = Stopwatch.StartNew();
        if (interval <= TimeSpan.Zero || interval > TimeSpan.FromSeconds(5))
            throw new ArgumentOutOfRangeException(nameof(interval));
        var safetyMetrics = new Dictionary<string, double>();
        void Observe(StabilityRuntimeSample sample)
        {
            safetyMetrics["safety.runtime.samples"] = safetyMetrics.GetValueOrDefault("safety.runtime.samples") + 1;
            safetyMetrics["safety.memory.available"] = sample.AvailableMemoryBytes.HasValue ? 1 : 0;
            if (sample.AvailableMemoryBytes is long bytes) safetyMetrics["safety.memory.last.bytes"] = bytes;
            safetyMetrics["safety.power.known"] = sample.SufficientPower.HasValue ? 1 : 0;
            safetyMetrics["safety.thermal.available"] = sample.ReliableThermalCritical.HasValue ? 1 : 0;
        }
        StabilityProbeResult Stop(string detail) => new(probe.Id, probe.Label, StabilityProbeStatus.Skipped,
            elapsed.Elapsed, detail, new Dictionary<string, double>(safetyMetrics));
        StabilityProbeResult WithMetrics(StabilityProbeResult result) => result with
        {
            Metrics = result.Metrics.Concat(safetyMetrics).GroupBy(item => item.Key)
                .ToDictionary(group => group.Key, group => group.Last().Value),
        };
        if (source is null) return Stop("No hay fuente de seguridad; no se genera carga.");
        DiagnosticReport refreshed;
        try { refreshed = await source.RefreshAsync(token).ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OperationCanceledException || !token.IsCancellationRequested)
        {
            return Stop("Revalidación no disponible: " + exception.GetType().Name + "; no se genera carga.");
        }
        var preflight = StabilityPreflightEvaluator.Evaluate(refreshed, HealthRuleEngine.Evaluate(refreshed));
        if (!preflight.CanRun) return Stop("Revalidación previa bloqueada: " +
            string.Join("; ", preflight.Checks.Where(check => check.Status is PreflightStatus.Block or PreflightStatus.Unknown)
                .Select(check => check.Detail)));
        context.StageBaseline = refreshed;
        safetyMetrics["safety.refresh.unix-ms"] = refreshed.CapturedAt.ToUnixTimeMilliseconds();
        safetyMetrics["safety.memory.before.bytes"] = refreshed.Sections.SelectMany(section => section.Data)
            .Single(datum => datum.MetricKey == "memory.available.bytes").NumericValue!.Value;
        var initialSample = await SampleSafelyAsync(source, token).ConfigureAwait(false);
        Observe(initialSample);
        var reason = StabilityRuntimePolicy.StopReason(initialSample);
        if (reason is not null) return Stop(reason);
        context.StageMemoryBudgetBytes = (long)Math.Min(1_073_741_824d,
            Math.Min(safetyMetrics["safety.memory.before.bytes"], initialSample.AvailableMemoryBytes!.Value) * 0.20d);
        safetyMetrics["safety.memory.budget.bytes"] = context.StageMemoryBudgetBytes.Value;

        token.ThrowIfCancellationRequested();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        // Task.Run ensures synchronous CPU/allocation work cannot starve the observer's startup.
        var workload = Task.Run(async () =>
        {
            stop.Token.ThrowIfCancellationRequested();
            return await probe.ExecuteAsync(context, stop.Token).ConfigureAwait(false);
        }, CancellationToken.None);
        var monitor = MonitorAsync(source, interval, initialSample.ReliableThermalCritical.HasValue, Observe, stop.Token);
        try
        {
            var first = await Task.WhenAny(workload, monitor).ConfigureAwait(false);
            if (first == monitor)
            {
                reason = await monitor.ConfigureAwait(false);
                stop.Cancel();
                try
                {
                    var stoppedResult = await workload.ConfigureAwait(false);
                    if (stoppedResult.Status == StabilityProbeStatus.Failed)
                        return WithMetrics(stoppedResult with { Detail = stoppedResult.Detail + " Seguridad: " + reason });
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
                token.ThrowIfCancellationRequested();
                return Stop("Carga detenida por seguridad: " + reason);
            }

            stop.Cancel();
            string? observed = null;
            try { observed = await monitor.ConfigureAwait(false); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
            var result = await workload.ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (observed is not null) return result.Status == StabilityProbeStatus.Failed
                ? WithMetrics(result with { Detail = result.Detail + " Seguridad: " + observed })
                : Stop("Carga detenida por seguridad: " + observed);
            return WithMetrics(result with
            {
                Detail = result.Detail +
                " Vigilancia de RAM/energía activa. Telemetría térmica " +
                (initialSample.ReliableThermalCritical.HasValue ? "fiable disponible." : "no disponible; no se presume temperatura segura.")
            });
        }
        finally
        {
            stop.Cancel();
            try { await monitor.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            try { await workload.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
    }

    private static async Task<string> MonitorAsync(IStabilitySafetySource source, TimeSpan interval,
        bool thermalRequired, Action<StabilityRuntimeSample> observe, CancellationToken token)
    {
        while (true)
        {
            await Task.Delay(interval, token).ConfigureAwait(false);
            var sample = await SampleSafelyAsync(source, token).ConfigureAwait(false);
            observe(sample);
            if (thermalRequired && sample.ReliableThermalCritical is null) return "Se perdió la telemetría térmica fiable.";
            var reason = StabilityRuntimePolicy.StopReason(sample);
            if (reason is not null) return reason;
        }
    }

    private static async Task<StabilityRuntimeSample> SampleSafelyAsync(IStabilitySafetySource source, CancellationToken token)
    {
        try { return await source.SampleAsync(token).ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OperationCanceledException || !token.IsCancellationRequested)
        { return new(null, null, null); }
    }
}
