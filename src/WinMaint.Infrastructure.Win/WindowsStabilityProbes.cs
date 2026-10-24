using System.Diagnostics;
using WinMaint.Application;
using WinMaint.Domain;

namespace WinMaint.Infrastructure.Win;

internal sealed class HardwareEventStabilityProbe(
    string id,
    string label,
    string metricKey,
    string? compareToMetric = null,
    Func<CancellationToken, Task<ControlledProcessResult>>? queryRunner = null) : IStabilityProbe
{
    private const string WheaQuery = "*[System[Provider[@Name='Microsoft-Windows-WHEA-Logger'] and TimeCreated[timediff(@SystemTime) <= 604800000]]]";

    public string Id { get; } = id;
    public string Label { get; } = label;
    public TimeSpan Timeout => TimeSpan.FromSeconds(40);

    public async Task<StabilityProbeResult> ExecuteAsync(
        StabilityProbeContext context,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        var executable = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "wevtutil.exe");
        string[] arguments = ["qe", "System", "/q:" + WheaQuery, "/f:xml", "/c:50", "/rd:true"];
        var query = queryRunner is null ? await ControlledProcessRunner.RunAsync(
            executable,
            arguments,
            TimeSpan.FromSeconds(30),
            cancellationToken).ConfigureAwait(false) : await queryRunner(cancellationToken).ConfigureAwait(false);
        if (query.TimedOut || query.ExitCode != 0)
        {
            return Result(StabilityProbeStatus.Skipped, started.Elapsed, "La consulta WHEA no estuvo disponible.");
        }

        double latestTimestamp;
        try
        {
            var events = WindowsEventXmlParser.Parse(query.StandardOutput);
            if (events.Count == 0 && !string.IsNullOrWhiteSpace(query.StandardOutput))
                return Result(StabilityProbeStatus.Skipped, started.Elapsed,
                    "La salida WHEA no vacía no contiene eventos interpretables; comparación no verificada.");
            if (events.Any(item => item.WheaKind == WheaEventKind.Unknown ||
                (item.WheaKind != WheaEventKind.Informational && item.CreatedAt is null)))
                return Result(StabilityProbeStatus.Skipped, started.Elapsed,
                    "La muestra WHEA tiene severidad o fecha no interpretable; comparación no verificada.");
            latestTimestamp = events
                .Where(item => item.WheaKind != WheaEventKind.Informational)
                .Select(item => item.CreatedAt?.ToUnixTimeMilliseconds() ?? 0)
                .DefaultIfEmpty(0)
                .Max();
        }
        catch (System.Xml.XmlException)
        {
            return Result(StabilityProbeStatus.Skipped, started.Elapsed, "La respuesta WHEA no tuvo el formato esperado.");
        }

        context.SharedMetrics[metricKey] = latestTimestamp;
        if (compareToMetric is not null && (!context.SharedMetrics.TryGetValue(compareToMetric, out var mark) ||
            !double.IsFinite(mark) || mark < 0))
            return Result(StabilityProbeStatus.Skipped, started.Elapsed,
                "Consulta posterior disponible, pero falta la marca WHEA previa; comparación no verificada.",
                new Dictionary<string, double> { [metricKey] = latestTimestamp });
        if (compareToMetric is not null
            && context.SharedMetrics.TryGetValue(compareToMetric, out var previous)
            && latestTimestamp > previous)
        {
            return Result(
                StabilityProbeStatus.Failed,
                started.Elapsed,
                "Apareció un evento WHEA más reciente; se omite carga intensiva posterior.",
                new Dictionary<string, double> { [metricKey] = latestTimestamp });
        }

        var detail = latestTimestamp == 0
            ? "Muestra consultada: sin WHEA de error o severidad desconocida; se excluyen los informativos."
            : $"Consulta completada; evento WHEA más reciente: {DateTimeOffset.FromUnixTimeMilliseconds((long)latestTimestamp):yyyy-MM-dd HH:mm:ss zzz}.";
        return Result(
            StabilityProbeStatus.Passed,
            started.Elapsed,
            detail,
            new Dictionary<string, double> { [metricKey] = latestTimestamp });
    }

    private StabilityProbeResult Result(
        StabilityProbeStatus status,
        TimeSpan duration,
        string detail,
        IReadOnlyDictionary<string, double>? metrics = null) =>
        new(Id, Label, status, duration, detail, metrics ?? new Dictionary<string, double>());
}

internal sealed class BaselineStabilityProbe(TimeSpan duration) : IStabilityProbe
{
    public string Id => "baseline";
    public string Label => "Línea base en reposo";
    public TimeSpan Timeout => duration + TimeSpan.FromSeconds(15);

    public async Task<StabilityProbeResult> ExecuteAsync(
        StabilityProbeContext context,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        await Task.Delay(duration, cancellationToken).ConfigureAwait(false);
        var memoryLoad = DiagnosticMetric(context.Baseline, "memory.load.percent");
        var cpuLoad = DiagnosticMetric(context.Baseline, "performance.cpu.percent");
        var metrics = new Dictionary<string, double>();
        if (memoryLoad is not null)
        {
            metrics["baseline.memory.percent"] = memoryLoad.Value;
        }

        if (cpuLoad is not null)
        {
            metrics["baseline.cpu.percent"] = cpuLoad.Value;
        }

        return new StabilityProbeResult(
            Id,
            Label,
            StabilityProbeStatus.Passed,
            started.Elapsed,
            "Periodo de reposo completado; las métricas iniciales se conservan por separado.",
            metrics);
    }

    private static double? DiagnosticMetric(DiagnosticReport report, string key) => report.Sections
        .SelectMany(section => section.Data)
        .FirstOrDefault(datum => datum.MetricKey == key)
        ?.NumericValue;
}

internal sealed class CpuStabilityProbe(TimeSpan duration) : IStabilityProbe
{
    public string Id => "cpu";
    public string Label => "CPU controlada";
    public TimeSpan Timeout => duration + TimeSpan.FromSeconds(15);

    public async Task<StabilityProbeResult> ExecuteAsync(
        StabilityProbeContext context,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        var workerCount = Math.Clamp(Environment.ProcessorCount / 4, 1, 4);
        var workers = Enumerable.Range(0, workerCount)
            .Select(index => RunWorkerAsync(index, duration, cancellationToken));
        var iterations = await Task.WhenAll(workers).ConfigureAwait(false);
        return new StabilityProbeResult(
            Id,
            Label,
            iterations.Sum() > 0 ? StabilityProbeStatus.Passed : StabilityProbeStatus.Skipped,
            started.Elapsed,
            $"Carga acotada al 50 % de ciclo en {workerCount} trabajadores completada; no se validó exactitud aritmética.",
            new Dictionary<string, double>
            {
                ["cpu.workers"] = workerCount,
                ["cpu.iterations"] = iterations.Sum(),
            });
    }

    private static async Task<double> RunWorkerAsync(
        int workerIndex,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var total = Stopwatch.StartNew();
        double iterations = 0;
        var accumulator = 1d + workerIndex;
        while (total.Elapsed < duration)
        {
            var active = Stopwatch.StartNew();
            while (active.Elapsed < TimeSpan.FromMilliseconds(50))
            {
                cancellationToken.ThrowIfCancellationRequested();
                accumulator = Math.Sqrt(accumulator + 1.000_001d);
                iterations++;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
        }

        GC.KeepAlive(accumulator);
        return iterations;
    }
}

internal sealed class MemoryPatternProbe(TimeSpan duration) : IStabilityProbe
{
    private const int ChunkSize = 16 * 1024 * 1024;
    private const long MaximumBytes = 1024L * 1024 * 1024;

    public string Id => "memory";
    public string Label => "Patrón de memoria";
    public TimeSpan Timeout => duration + TimeSpan.FromSeconds(30);

    public async Task<StabilityProbeResult> ExecuteAsync(
        StabilityProbeContext context,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        var target = Math.Min(CalculateTarget(context.StageBaseline), context.StageMemoryBudgetBytes ?? 0);
        if (target < ChunkSize)
        {
            return Result(StabilityProbeStatus.Skipped, started.Elapsed, "No hay una medición reciente suficiente para una muestra segura.", 0, 0);
        }

        var chunks = new List<byte[]>();
        long allocated = 0;
        long passes = 0;
        try
        {
            while (allocated < target)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var size = (int)Math.Min(ChunkSize, target - allocated);
                chunks.Add(GC.AllocateUninitializedArray<byte>(size));
                allocated += size;
            }

            var deadline = DateTimeOffset.UtcNow + duration;
            while (DateTimeOffset.UtcNow < deadline)
            {
                for (var chunkIndex = 0; chunkIndex < chunks.Count; chunkIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    FillAndVerify(chunks[chunkIndex], chunkIndex, passes);
                }

                passes++;
                await Task.Yield();
            }

            return Result(
                StabilityProbeStatus.Passed,
                started.Elapsed,
                "Los patrones escritos en memoria de proceso se verificaron sin diferencias.",
                allocated,
                passes);
        }
        catch (OutOfMemoryException)
        {
            return Result(
                StabilityProbeStatus.Skipped,
                started.Elapsed,
                "Windows no concedió la muestra calculada; no se interpreta como fallo físico.",
                allocated,
                passes);
        }
        catch (InvalidDataException exception)
        {
            return Result(StabilityProbeStatus.Failed, started.Elapsed, exception.Message, allocated, passes);
        }
        finally
        {
            chunks.Clear();
        }
    }

    private static void FillAndVerify(byte[] buffer, int chunkIndex, long pass)
    {
        var seed = unchecked((byte)(chunkIndex * 31 + pass));
        for (var index = 0; index < buffer.Length; index++)
        {
            buffer[index] = unchecked((byte)(seed + index));
        }

        for (var index = 0; index < buffer.Length; index++)
        {
            var expected = unchecked((byte)(seed + index));
            if (buffer[index] != expected)
            {
                throw new InvalidDataException($"Diferencia de memoria en bloque {chunkIndex}, desplazamiento {index}.");
            }
        }
    }

    internal static long CalculateTarget(DiagnosticReport? report)
    {
        var values = report?.Sections
            .SelectMany(section => section.Data)
            .Where(datum => datum.MetricKey == "memory.available.bytes" && datum.Coverage != EvidenceCoverage.Absent).ToArray();
        var value = values is { Length: 1 } ? values[0].NumericValue : null;
        return value is double available && double.IsFinite(available) && available >= 0 && available < long.MaxValue
            ? Math.Min(MaximumBytes, (long)(available * 0.20d)) : 0;
    }

    private StabilityProbeResult Result(
        StabilityProbeStatus status,
        TimeSpan elapsed,
        string detail,
        long bytes,
        long passes) => new(
            Id,
            Label,
            status,
            elapsed,
            detail,
            new Dictionary<string, double>
            {
                ["memory.bytes"] = bytes,
                ["memory.passes"] = passes,
            });
}

internal sealed class StorageReadProbe(TimeSpan duration) : IStabilityProbe
{
    public string Id => "storage-read";
    public string Label => "Lectura secuencial de almacenamiento";
    public TimeSpan Timeout => duration + TimeSpan.FromSeconds(20);

    public async Task<StabilityProbeResult> ExecuteAsync(
        StabilityProbeContext context,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
        {
            return Result(StabilityProbeStatus.Skipped, started.Elapsed, "No se encontró un archivo propio seguro para la lectura.", 0);
        }

        var buffer = new byte[1024 * 1024];
        long bytesRead = 0;
        try
        {
            await using var stream = new FileStream(
                executable,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                buffer.Length,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var deadline = DateTimeOffset.UtcNow + duration;
            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (count == 0)
                {
                    stream.Position = 0;
                    continue;
                }

                bytesRead += count;
            }

            return Result(
                StabilityProbeStatus.Passed,
                started.Elapsed,
                "Lectura del ejecutable completada; no se creó ni escribió un archivo de benchmark.",
                bytesRead);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Result(StabilityProbeStatus.Failed, started.Elapsed, "La lectura terminó con un error controlado.", bytesRead);
        }
    }

    private StabilityProbeResult Result(
        StabilityProbeStatus status,
        TimeSpan elapsed,
        string detail,
        long bytesRead) => new(
            Id,
            Label,
            status,
            elapsed,
            detail,
            new Dictionary<string, double> { ["storage.bytes-read"] = bytesRead });
}

internal sealed class CoolDownProbe(TimeSpan duration) : IStabilityProbe
{
    public string Id => "cooldown";
    public string Label => "Enfriamiento";
    public TimeSpan Timeout => duration + TimeSpan.FromSeconds(15);

    public async Task<StabilityProbeResult> ExecuteAsync(
        StabilityProbeContext context,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        await Task.Delay(duration, cancellationToken).ConfigureAwait(false);
        return new StabilityProbeResult(
            Id,
            Label,
            StabilityProbeStatus.Passed,
            started.Elapsed,
            "Periodo sin carga generada por WinMaint completado. Temperatura no disponible no se interpreta como segura.",
            new Dictionary<string, double>());
    }
}

public static class WindowsStabilityComposition
{
    public static StabilityTestCoordinator Create(
        IStabilitySessionStore sessionStore,
        StabilityTestOptions? options = null)
    {
        options ??= StabilityTestOptions.Standard;
        IStabilityProbe[] probes =
        [
            new HardwareEventStabilityProbe("hardware-events-before", "Eventos WHEA previos", "whea.latest.before"),
            new BaselineStabilityProbe(options.BaselineDuration),
            new CpuStabilityProbe(options.CpuDuration),
            new HardwareEventStabilityProbe("hardware-events-after-cpu", "WHEA después de CPU", "whea.latest.after-cpu", "whea.latest.before"),
            new MemoryPatternProbe(options.MemoryDuration),
            new HardwareEventStabilityProbe("hardware-events-after-memory", "WHEA después de RAM", "whea.latest.after-memory", "whea.latest.before"),
            new StorageReadProbe(options.StorageReadDuration),
            new HardwareEventStabilityProbe("hardware-events-after-storage", "WHEA después de almacenamiento", "whea.latest.after-storage", "whea.latest.before"),
            new CoolDownProbe(options.CoolDownDuration),
            new HardwareEventStabilityProbe("hardware-events-after", "Eventos WHEA posteriores", "whea.latest.after", "whea.latest.before"),
        ];
        return new StabilityTestCoordinator(probes, sessionStore, options, new WindowsStabilitySafetySource());
    }
}
