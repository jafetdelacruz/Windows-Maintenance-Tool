using WinMaint.Application;
using WinMaint.Domain;

namespace WinMaint.Application.Tests;

public sealed class StabilityCoordinatorTests
{
    [Fact]
    public void StandardPlanSumsFourToSixMinutesButDoesNotMeasureRuntime()
    {
        Assert.InRange(StabilityTestOptions.Standard.PlannedDuration, TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(6));
    }

    [Fact]
    public async Task PriorityZeroDoesNotStartSessionOrProbe()
    {
        var report = SafeReport(DiagnosticDatum.Available(
            "Salud del disco",
            "Unhealthy",
            "fixture",
            metricKey: "storage.disk.health",
            textValue: "Unhealthy"));
        var assessment = HealthRuleEngine.Evaluate(report);
        var probe = new FakeProbe("cpu", StabilityProbeStatus.Passed);
        var store = new MemorySessionStore();
        var coordinator = new StabilityTestCoordinator([probe], store, ShortOptions(), new SafeSafetySource(report));

        var result = await coordinator.RunAsync(report, assessment);

        Assert.True(result.Preflight.HasBlock);
        Assert.False(store.Started);
        Assert.False(probe.WasCalled);
    }

    [Fact]
    public async Task FakePlanInvokesEveryProbeAndDeclaresBenchmarkWritesDisabled()
    {
        var report = SafeReport();
        var assessment = HealthRuleEngine.Evaluate(report);
        var probes = new IStabilityProbe[]
        {
            new FakeProbe("cpu", StabilityProbeStatus.Passed),
            new FakeProbe("memory", StabilityProbeStatus.Passed),
            new FakeProbe("storage-read", StabilityProbeStatus.Passed),
        };
        var store = new MemorySessionStore();
        var coordinator = new StabilityTestCoordinator(probes, store, ShortOptions(), new SafeSafetySource(report));

        var result = await coordinator.RunAsync(report, assessment);

        Assert.True(store.Started);
        Assert.True(store.Completed);
        Assert.All(probes.Cast<FakeProbe>(), probe => Assert.True(probe.WasCalled));
        Assert.False(result.WroteBenchmarkData);
        Assert.False(result.WasInterrupted);
    }

    [Fact]
    public async Task CancellationLeavesRecoverableInterruptedSession()
    {
        var report = SafeReport();
        var assessment = HealthRuleEngine.Evaluate(report);
        var store = new MemorySessionStore();
        var coordinator = new StabilityTestCoordinator(
            [new CancellingProbe()],
            store,
            ShortOptions());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

        var result = await coordinator.RunAsync(report, assessment, cancellationToken: cancellation.Token);

        Assert.True(result.WasInterrupted);
        Assert.True(store.Started);
        Assert.False(store.Completed);
        Assert.Equal(StabilityProbeStatus.Cancelled, Assert.Single(result.Probes).Status);
    }

    private static StabilityTestOptions ShortOptions() => new(
        TimeSpan.Zero,
        TimeSpan.Zero,
        TimeSpan.Zero,
        TimeSpan.Zero,
        TimeSpan.Zero);

    private static DiagnosticReport SafeReport(params DiagnosticDatum[] additions)
    {
        var data = new List<DiagnosticDatum>
        {
            Datum("power.source", text: "Corriente alterna"),
            Datum("storage.volume.free.percent", number: 50),
            Datum("storage.disk.health", text: "Healthy"),
            Datum("storage.disk.operational", text: "OK"),
            Datum("performance.cpu.percent", number: 10, coverage: EvidenceCoverage.Partial),
            Datum("memory.available.bytes", number: 4L * 1024 * 1024 * 1024),
            Datum("memory.load.percent", number: 30),
            Datum("events.whea.error.count", number: 0, coverage: EvidenceCoverage.Partial),
        };
        data.AddRange(additions);
        return new DiagnosticReport(
            DateTimeOffset.UtcNow,
            [new DiagnosticSection("fixture", "Fixture", EvidenceCoverage.Partial, data, [])],
            IsReadOnly: true);
    }

    private static DiagnosticDatum Datum(
        string key,
        double? number = null,
        string? text = null,
        EvidenceCoverage coverage = EvidenceCoverage.Complete) =>
        DiagnosticDatum.Available(
            key,
            text ?? number?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "ok",
            "fixture",
            coverage,
            metricKey: key,
            numericValue: number,
            textValue: text);

    private sealed class FakeProbe(string id, StabilityProbeStatus status) : IStabilityProbe
    {
        public string Id { get; } = id;
        public string Label => Id;
        public TimeSpan Timeout => TimeSpan.FromSeconds(1);
        public bool WasCalled { get; private set; }

        public Task<StabilityProbeResult> ExecuteAsync(
            StabilityProbeContext context,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            return Task.FromResult(new StabilityProbeResult(
                Id,
                Label,
                status,
                TimeSpan.Zero,
                "fixture",
                new Dictionary<string, double>()));
        }
    }

    private sealed class CancellingProbe : IStabilityProbe
    {
        public string Id => "cancel";
        public string Label => "Cancel";
        public TimeSpan Timeout => TimeSpan.FromSeconds(1);

        public async Task<StabilityProbeResult> ExecuteAsync(
            StabilityProbeContext context,
            CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            throw new InvalidOperationException("Unreachable");
        }
    }

    private sealed class MemorySessionStore : IStabilitySessionStore
    {
        public bool Started { get; private set; }
        public bool Completed { get; private set; }

        public Task<InterruptedStabilitySession?> ReadInterruptedAsync(CancellationToken cancellationToken) =>
            Task.FromResult<InterruptedStabilitySession?>(Started && !Completed
                ? new InterruptedStabilitySession(DateTimeOffset.UtcNow, null)
                : null);

        public Task StartAsync(DateTimeOffset startedAt, CancellationToken cancellationToken)
        {
            Started = true;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(string completedProbeId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task CompleteAsync(CancellationToken cancellationToken)
        {
            Completed = true;
            return Task.CompletedTask;
        }
    }

    private sealed class SafeSafetySource(DiagnosticReport report) : IStabilitySafetySource
    {
        public Task<DiagnosticReport> RefreshAsync(CancellationToken cancellationToken) => Task.FromResult(report);
        public Task<StabilityRuntimeSample> SampleAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new StabilityRuntimeSample(4L * 1024 * 1024 * 1024, true, null));
    }
}
