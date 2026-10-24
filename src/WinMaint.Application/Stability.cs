using WinMaint.Domain;

namespace WinMaint.Application;

public sealed record StabilityTestOptions(
    TimeSpan BaselineDuration,
    TimeSpan CpuDuration,
    TimeSpan MemoryDuration,
    TimeSpan StorageReadDuration,
    TimeSpan CoolDownDuration)
{
    public static StabilityTestOptions Standard { get; } = new(
        TimeSpan.FromSeconds(45),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(50),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(90));

    public TimeSpan PlannedDuration =>
        BaselineDuration + CpuDuration + MemoryDuration + StorageReadDuration + CoolDownDuration;
}

public sealed record StabilityProgress(string ProbeId, string Label, string State, TimeSpan Elapsed);

public sealed class StabilityProbeContext(
    DiagnosticReport baseline,
    StabilityTestOptions options)
{
    public DiagnosticReport Baseline { get; } = baseline;
    public DiagnosticReport? StageBaseline { get; internal set; }
    public long? StageMemoryBudgetBytes { get; internal set; }
    public StabilityTestOptions Options { get; } = options;
    public Dictionary<string, double> SharedMetrics { get; } = [];
}

public interface IStabilityProbe
{
    string Id { get; }
    string Label { get; }
    TimeSpan Timeout { get; }
    Task<StabilityProbeResult> ExecuteAsync(
        StabilityProbeContext context,
        CancellationToken cancellationToken);
}

public interface IStabilitySessionStore
{
    Task<InterruptedStabilitySession?> ReadInterruptedAsync(CancellationToken cancellationToken);
    Task StartAsync(DateTimeOffset startedAt, CancellationToken cancellationToken);
    Task UpdateAsync(string completedProbeId, CancellationToken cancellationToken);
    Task CompleteAsync(CancellationToken cancellationToken);
}

public sealed record InterruptedStabilitySession(DateTimeOffset StartedAt, string? LastCompletedProbeId);

public static class StabilityPreflightEvaluator
{
    public static StabilityPreflight Evaluate(DiagnosticReport report, HealthAssessment assessment)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(assessment);
        var data = report.Sections.SelectMany(section => section.Data).ToArray();
        var checks = new List<PreflightCheck>();

        checks.Add(assessment.HasPriorityZero
            ? new("priority-zero", "Hallazgos P0", PreflightStatus.Block, "Un hallazgo P0 bloquea toda carga intensiva.")
            : new("priority-zero", "Hallazgos P0", PreflightStatus.Pass, "No hay hallazgos P0 en la evidencia actual."));
        checks.Add(EvaluatePower(data));
        checks.Add(EvaluateStorage(data));
        checks.Add(EvaluateDiskState(data));
        checks.Add(EvaluateInitialLoad(data));
        checks.Add(EvaluateMemory(data));
        checks.Add(EvaluateWhea(data));
        if (assessment.Findings.Any(finding => finding.RuleId == "MEMORY-DIAGNOSTIC-FAILED"))
            checks.Add(new("memory-prior-failure", "Errores previos de memoria", PreflightStatus.Block,
                "Hay errores previos de memoria pendientes de revisión; no se iniciará carga intensiva."));
        if (checks.Any(check => check.Status == PreflightStatus.Unknown))
            checks.Add(new("required-evidence", "Requisitos indispensables", PreflightStatus.Block,
                "Faltan datos válidos de seguridad. No se iniciará carga; esto no implica un fallo físico."));
        checks.Add(new(
            "open-work",
            "Trabajo abierto",
            PreflightStatus.Warning,
            "Guarda tu trabajo antes de iniciar; WinMaint no puede comprobar aplicaciones con cambios pendientes."));

        return new StabilityPreflight(checks, !checks.Any(check => check.Status == PreflightStatus.Block));
    }

    private static PreflightCheck EvaluatePower(IReadOnlyList<DiagnosticDatum> data)
    {
        var source = Metric(data, "power.source");
        var battery = Metric(data, "power.battery.percent");
        if (source?.TextValue == "Batería")
        {
            if (!ValidNumber(battery, 0, 100))
            {
                return new("power", "Energía", PreflightStatus.Block, "En batería sin porcentaje fiable; conecta corriente alterna.");
            }

            if (battery!.NumericValue < 40)
            {
                return new("power", "Energía", PreflightStatus.Block, "La batería está por debajo de 40 %; conecta corriente alterna.");
            }

            return new("power", "Energía", PreflightStatus.Warning, "Funcionando con batería; se recomienda corriente alterna.");
        }

        return source?.TextValue == "Corriente alterna"
            ? new("power", "Energía", PreflightStatus.Pass, "Corriente alterna detectada.")
            : new("power", "Energía", PreflightStatus.Unknown, "No se pudo confirmar la fuente de energía.");
    }

    private static PreflightCheck EvaluateStorage(IReadOnlyList<DiagnosticDatum> data)
    {
        var volumes = Metrics(data, "storage.volume.free.percent").ToArray();
        if (volumes.Length == 0)
        {
            return new("storage", "Espacio y discos", PreflightStatus.Unknown, "No se pudo confirmar el espacio libre.");
        }

        if (volumes.Any(item => !ValidNumber(item, 0, 100)))
            return new("storage", "Espacio y discos", PreflightStatus.Unknown, "Hay volúmenes sin un porcentaje libre válido.");
        var minimum = volumes.Min(item => item.NumericValue!.Value);
        return minimum < 5
            ? new("storage", "Espacio y discos", PreflightStatus.Block, "Un volumen tiene menos de 5 % libre.")
            : new("storage", "Espacio y discos", PreflightStatus.Pass, $"Espacio mínimo observado: {minimum:0.0} %.");
    }

    private static PreflightCheck EvaluateDiskState(IReadOnlyList<DiagnosticDatum> data)
    {
        var states = Metrics(data, "storage.disk.operational").ToArray();
        var health = Metrics(data, "storage.disk.health").ToArray();
        var normal = states.Length > 0 && states.All(item => item.Coverage != EvidenceCoverage.Absent &&
            DiskOperationalPolicy.Classify(item.TextValue) == DiskOperationalState.Normal) &&
            health.Length > 0 && health.All(item => item.Coverage != EvidenceCoverage.Absent &&
                string.Equals(item.TextValue, "Healthy", StringComparison.OrdinalIgnoreCase));
        return normal
            ? new("disk-state", "Estado de discos", PreflightStatus.Pass, "Estados conocidos normales en la muestra.")
            : new("disk-state", "Estado de discos", PreflightStatus.Block,
                "No se confirmó un estado normal para todos los discos observados; revisar antes de generar carga.");
    }

    private static PreflightCheck EvaluateInitialLoad(IReadOnlyList<DiagnosticDatum> data)
    {
        var datum = Metric(data, "performance.cpu.percent");
        var value = ValidNumber(datum, 0, 100) ? datum!.NumericValue : null;
        return value switch
        {
            null => new("initial-load", "Carga inicial", PreflightStatus.Unknown, "No se pudo medir la carga inicial."),
            >= 90 => new("initial-load", "Carga inicial", PreflightStatus.Block, $"CPU en {value:0.0} %; cierra tareas intensivas y vuelve a medir."),
            >= 70 => new("initial-load", "Carga inicial", PreflightStatus.Warning, $"CPU en {value:0.0} %; el resultado puede variar."),
            _ => new("initial-load", "Carga inicial", PreflightStatus.Pass, $"CPU inicial en {value:0.0} %.")
        };
    }

    private static PreflightCheck EvaluateMemory(IReadOnlyList<DiagnosticDatum> data)
    {
        var datum = Metric(data, "memory.available.bytes");
        var bytes = ValidNumber(datum, 0, long.MaxValue) && datum!.NumericValue < long.MaxValue ? datum.NumericValue : null;
        return bytes switch
        {
            null => new("memory", "Memoria disponible", PreflightStatus.Unknown, "No se pudo medir la memoria disponible."),
            < 268_435_456 => new("memory", "Memoria disponible", PreflightStatus.Block, "Hay menos de 256 MiB disponibles."),
            < 1_073_741_824 => new("memory", "Memoria disponible", PreflightStatus.Warning, "Hay menos de 1 GiB disponible; la muestra se reducirá."),
            _ => new("memory", "Memoria disponible", PreflightStatus.Pass, "Memoria suficiente para una muestra limitada.")
        };
    }

    private static PreflightCheck EvaluateWhea(IReadOnlyList<DiagnosticDatum> data)
    {
        var unknown = Metric(data, "events.whea.unknown.count");
        if (Metrics(data, "events.whea.unknown.count").Any() &&
            (!ValidCount(unknown) || unknown!.NumericValue > 0))
        {
            return new("whea", "Eventos WHEA", PreflightStatus.Unknown,
                "Hay registros WHEA sin severidad interpretable; requieren revisión.");
        }

        var datum = Metric(data, "events.whea.error.count");
        return (ValidCount(datum) ? datum!.NumericValue : null) switch
        {
            null => new("whea", "Eventos WHEA", PreflightStatus.Unknown, "No se pudo completar la consulta WHEA."),
            > 0 => new("whea", "Eventos WHEA", PreflightStatus.Warning, $"Se observaron {datum!.NumericValue:0} eventos recientes; se comparará antes/después."),
            _ => new("whea", "Eventos WHEA", PreflightStatus.Pass, "No se observaron errores o advertencias WHEA en la muestra consultada.")
        };
    }

    private static bool ValidNumber(DiagnosticDatum? datum, double minimum, double maximum) =>
        datum is { Coverage: not EvidenceCoverage.Absent, NumericValue: double value } &&
        double.IsFinite(value) && value >= minimum && value <= maximum;

    private static bool ValidCount(DiagnosticDatum? datum) => ValidNumber(datum, 0, long.MaxValue) &&
        datum!.NumericValue < long.MaxValue && datum.NumericValue == Math.Truncate(datum.NumericValue!.Value);

    private static DiagnosticDatum? Metric(IEnumerable<DiagnosticDatum> data, string key)
    {
        var values = Metrics(data, key).ToArray();
        return values.Length == 1 && values[0].Coverage != EvidenceCoverage.Absent ? values[0] : null;
    }

    private static IEnumerable<DiagnosticDatum> Metrics(IEnumerable<DiagnosticDatum> data, string key) =>
        data.Where(item => item.MetricKey == key);
}

public sealed class StabilityTestCoordinator(
    IEnumerable<IStabilityProbe> probes,
    IStabilitySessionStore sessionStore,
    StabilityTestOptions options,
    IStabilitySafetySource? safetySource = null,
    TimeSpan? safetyInterval = null)
{
    private readonly IStabilityProbe[] probes = probes.ToArray();

    public async Task<StabilityTestResult> RunAsync(
        DiagnosticReport baseline,
        HealthAssessment assessment,
        IProgress<StabilityProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var preflight = StabilityPreflightEvaluator.Evaluate(baseline, assessment);
        var startedAt = DateTimeOffset.Now;
        if (!preflight.CanRun)
        {
            return new StabilityTestResult(startedAt, DateTimeOffset.Now, preflight, [], false, false, []);
        }

        await sessionStore.StartAsync(startedAt, cancellationToken).ConfigureAwait(false);
        var context = new StabilityProbeContext(baseline, options);
        var results = new List<StabilityProbeResult>();
        var interrupted = false;
        var skipIntensive = false;

        for (var index = 0; index < probes.Length; index++)
        {
            var probe = probes[index];
            if (skipIntensive && probe.Id is "cpu" or "memory" or "storage-read")
            {
                results.Add(new StabilityProbeResult(
                    probe.Id,
                    probe.Label,
                    StabilityProbeStatus.Skipped,
                    TimeSpan.Zero,
                    "Omitida porque una etapa previa no terminó de forma segura.",
                    new Dictionary<string, double>()));
                continue;
            }

            progress?.Report(new StabilityProgress(probe.Id, probe.Label, "Iniciando", DateTimeOffset.Now - startedAt));
            var probeStartedAt = DateTimeOffset.Now;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(probe.Timeout);
            StabilityProbeResult result;
            try
            {
                result = probe.Id is "cpu" or "memory" or "storage-read"
                    ? await GuardedStabilityExecution.RunAsync(probe, context, safetySource,
                        safetyInterval ?? TimeSpan.FromSeconds(1), timeout.Token).ConfigureAwait(false)
                    : await probe.ExecuteAsync(context, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                result = new StabilityProbeResult(
                    probe.Id,
                    probe.Label,
                    StabilityProbeStatus.TimedOut,
                    DateTimeOffset.Now - probeStartedAt,
                    "La etapa excedió su timeout.",
                    new Dictionary<string, double>());
            }
            catch (OperationCanceledException)
            {
                interrupted = true;
                result = new StabilityProbeResult(
                    probe.Id,
                    probe.Label,
                    StabilityProbeStatus.Cancelled,
                    DateTimeOffset.Now - probeStartedAt,
                    "Cancelación solicitada; la sesión queda marcada como interrumpida.",
                    new Dictionary<string, double>());
            }

            results.Add(result);
            progress?.Report(new StabilityProgress(probe.Id, probe.Label, result.Status.ToString(), DateTimeOffset.Now - startedAt));
            if (interrupted)
            {
                foreach (var remaining in probes.Skip(index + 1))
                {
                    results.Add(new StabilityProbeResult(
                        remaining.Id,
                        remaining.Label,
                        StabilityProbeStatus.Skipped,
                        TimeSpan.Zero,
                        "Omitida después de la cancelación.",
                        new Dictionary<string, double>()));
                }

                break;
            }

            await sessionStore.UpdateAsync(probe.Id, cancellationToken).ConfigureAwait(false);
            if (result.Status is StabilityProbeStatus.Failed or StabilityProbeStatus.TimedOut or StabilityProbeStatus.Skipped)
            {
                skipIntensive = true;
            }
        }

        var findings = CreateFindings(results, context.SharedMetrics);
        if (!interrupted)
        {
            await sessionStore.CompleteAsync(CancellationToken.None).ConfigureAwait(false);
        }

        return new StabilityTestResult(
            startedAt,
            DateTimeOffset.Now,
            preflight,
            results,
            interrupted,
            WroteBenchmarkData: false,
            findings);
    }

    private static List<DiagnosticFinding> CreateFindings(
        IEnumerable<StabilityProbeResult> results,
        Dictionary<string, double> metrics)
    {
        var findings = new List<DiagnosticFinding>();
        foreach (var result in results)
        {
            if (result.Status == StabilityProbeStatus.Failed)
            {
                var priority = result.ProbeId is "memory" or "storage-read" ? Priority.P0 : Priority.P1;
                findings.Add(new DiagnosticFinding(
                    "STABILITY-" + result.ProbeId.ToUpperInvariant() + "-FAILED",
                    HealthRuleEngine.RuleSetVersion,
                    HealthDomain.LoadStability,
                    priority,
                    RiskLevel.R0,
                    $"Falló la etapa {result.Label}.",
                    "Detén pruebas adicionales y revisa la evidencia antes de cualquier mantenimiento.",
                    result.Detail));
            }
            else if (result.Status == StabilityProbeStatus.TimedOut)
            {
                findings.Add(new DiagnosticFinding(
                    "STABILITY-" + result.ProbeId.ToUpperInvariant() + "-TIMEOUT",
                    HealthRuleEngine.RuleSetVersion,
                    HealthDomain.LoadStability,
                    Priority.P2,
                    RiskLevel.R0,
                    $"La etapa {result.Label} agotó su tiempo.",
                    "Repite la prueba con el equipo en reposo y revisa la respuesta del sistema.",
                    result.Detail));
            }
        }

        if (!results.Any(result =>
                result.ProbeId.StartsWith("hardware-events", StringComparison.Ordinal)
                && result.Status == StabilityProbeStatus.Failed)
            && metrics.TryGetValue("whea.latest.before", out var before)
            && metrics.TryGetValue("whea.latest.after", out var after)
            && after > before)
        {
            findings.Add(new DiagnosticFinding(
                "STABILITY-NEW-WHEA",
                HealthRuleEngine.RuleSetVersion,
                HealthDomain.LoadStability,
                Priority.P1,
                RiskLevel.R0,
                "Aparecieron nuevos eventos WHEA durante la prueba.",
                "Detén cargas adicionales y revisa hardware, firmware y controladores.",
                "La marca temporal del evento WHEA más reciente aumentó durante la prueba."));
        }

        return findings;
    }
}
