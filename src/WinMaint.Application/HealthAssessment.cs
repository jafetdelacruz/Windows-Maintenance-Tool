using WinMaint.Domain;

namespace WinMaint.Application;

public static class HealthRuleEngine
{
    public const string RuleSetVersion = "1.0.4";

    private const int MinimumDomainConfidence = 35;
    private static readonly string[] RequiredWheaProbes = ["hardware-events-before", "hardware-events-after"];

    private static readonly Dictionary<HealthDomain, int> DomainWeights =
        new Dictionary<HealthDomain, int>
        {
            [HealthDomain.HardwareReliability] = 30,
            [HealthDomain.Storage] = 25,
            [HealthDomain.Memory] = 20,
            [HealthDomain.WindowsIntegrity] = 15,
            [HealthDomain.LoadStability] = 10,
        };

    public static HealthAssessment Evaluate(
        DiagnosticReport report,
        StabilityTestResult? stabilityResult = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        var data = report.Sections.SelectMany(section => section.Data).ToArray();
        var findings = EvaluateDiagnosticRules(data).ToList();
        if (stabilityResult is not null)
        {
            findings.AddRange(stabilityResult.Findings);
        }

        var confidence = CalculateConfidence(data, stabilityResult);
        return BuildAssessment(confidence, findings, RuleSetVersion) with
        { MemoryEvidence = MemoryEvidencePolicy.Evaluate(data, stabilityResult) };
    }

    // A later snapshot or cancelled test is not evidence that an earlier fault was resolved.
    // Historical assessments keep their original rules and findings; no source is reinterpreted.
    public static HealthAssessment? EvaluateSession(
        IReadOnlyList<HealthAssessment> snapshots,
        IReadOnlyList<StabilityTestResult> stabilityTests)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(stabilityTests);
        if (snapshots.Count == 0) return null;
        var latest = snapshots[^1];
        var confidence = Enum.GetValues<HealthDomain>().ToDictionary(domain => domain,
            domain => latest.Domains.FirstOrDefault(item => item.Domain == domain)?.ConfidencePercent ?? 0);
        confidence[HealthDomain.LoadStability] = StabilityConfidence(stabilityTests.Count == 0 ? null : stabilityTests[^1]);
        var findings = snapshots.SelectMany(snapshot => snapshot.Findings)
            .Concat(stabilityTests.SelectMany(test => test.Findings))
            .GroupBy(finding => (finding.RuleId, finding.Evidence))
            .Select(group => group.OrderBy(finding => finding.Priority).First()).ToArray();
        var memory = latest.MemoryEvidence;
        if (memory is not null && findings.Any(finding => finding.RuleId == "MEMORY-DIAGNOSTIC-FAILED"))
            memory = memory with { PhysicalState = MemoryPhysicalState.PreviousFailureRequiresReview };
        return BuildAssessment(confidence, findings, latest.RuleSetVersion) with { MemoryEvidence = memory };
    }

    private static HealthAssessment BuildAssessment(
        Dictionary<HealthDomain, int> confidence,
        IReadOnlyList<DiagnosticFinding> findings,
        string ruleSetVersion)
    {
        var penalties = findings
            .GroupBy(finding => finding.Domain)
            .ToDictionary(group => group.Key, group => group.Sum(PenaltyFor));
        var domains = Enum.GetValues<HealthDomain>()
            .Select(domain => CreateDomainScore(domain, confidence[domain], penalties.GetValueOrDefault(domain)))
            .ToArray();

        var scoredDomains = domains.Where(domain => domain.HasSufficientEvidence).ToArray();
        int? overall = scoredDomains.Length == 0
            ? null
            : (int)Math.Round(
                scoredDomains.Sum(domain => domain.Score!.Value * DomainWeights[domain.Domain])
                / (double)scoredDomains.Sum(domain => DomainWeights[domain.Domain]),
                MidpointRounding.AwayFromZero);

        if (overall is not null && findings.Any(finding => finding.Priority == Priority.P0))
        {
            overall = Math.Min(overall.Value, 39);
        }
        else if (overall is not null && findings.Any(finding => finding.Priority == Priority.P1))
        {
            overall = Math.Min(overall.Value, 59);
        }

        var overallConfidence = (int)Math.Round(
            confidence.Sum(item => item.Value * DomainWeights[item.Key]) / 100d,
            MidpointRounding.AwayFromZero);

        return new HealthAssessment(
            ruleSetVersion,
            overall,
            overallConfidence,
            BandFor(overall),
            domains,
            findings.OrderBy(finding => finding.Priority).ThenBy(finding => finding.RuleId).ToArray());
    }

    private static IEnumerable<DiagnosticFinding> EvaluateDiagnosticRules(
        IReadOnlyList<DiagnosticDatum> data)
    {
        if (MemoryEvidencePolicy.Evaluate(data).PreviousDiagnosticStatus == "failed")
            yield return Finding("MEMORY-DIAGNOSTIC-FAILED", HealthDomain.Memory, Priority.P1,
                "Windows Memory Diagnostic registró errores en una prueba previa.",
                "Prioriza respaldo y diagnóstico de memoria; el registro previo no identifica por sí solo el módulo ni acredita su estado actual.",
                data.Single(datum => datum.MetricKey == "memory.diagnostic.previous.status"));

        foreach (var datum in Metrics(data, "storage.disk.health"))
        {
            if (datum.Coverage == EvidenceCoverage.Absent) continue;
            if (datum.TextValue?.Equals("Unhealthy", StringComparison.OrdinalIgnoreCase) == true)
            {
                yield return Finding(
                    "STORAGE-HEALTH-UNHEALTHY",
                    HealthDomain.HardwareReliability,
                    Priority.P0,
                    "Windows reporta un disco físico no saludable.",
                    "Evita carga intensiva, prioriza respaldo y diagnóstico del fabricante.",
                    datum);
            }
            else if (datum.TextValue?.Equals("Warning", StringComparison.OrdinalIgnoreCase) == true)
            {
                yield return Finding(
                    "STORAGE-HEALTH-WARNING",
                    HealthDomain.HardwareReliability,
                    Priority.P1,
                    "Windows reporta advertencia en un disco físico.",
                    "Revisa el disco y respalda información importante antes de mantenimiento.",
                    datum);
            }
        }

        foreach (var datum in Metrics(data, "storage.disk.operational"))
        {
            if (datum.Coverage != EvidenceCoverage.Absent &&
                DiskOperationalPolicy.Classify(datum.TextValue) == DiskOperationalState.Adverse)
            {
                yield return Finding(
                    "STORAGE-OPERATIONAL-NOT-OK",
                    HealthDomain.HardwareReliability,
                    Priority.P0,
                    "Un disco físico no está en estado operativo normal.",
                    "Detén pruebas intensivas y revisa el almacenamiento antes de continuar.",
                    datum);
            }
        }

        foreach (var datum in Metrics(data, "storage.volume.free.percent"))
        {
            if (datum.Coverage == EvidenceCoverage.Absent || datum.NumericValue is not double free ||
                !double.IsFinite(free) || free is < 0 or > 100) continue;
            if (datum.NumericValue is < 5)
            {
                yield return Finding(
                    "STORAGE-FREE-CRITICAL",
                    HealthDomain.Storage,
                    Priority.P1,
                    "Un volumen tiene menos de 5 % de espacio libre.",
                    "Libera espacio con un plan revisado antes de tareas prolongadas.",
                    datum);
            }
            else if (datum.NumericValue is < 10)
            {
                yield return Finding(
                    "STORAGE-FREE-LOW",
                    HealthDomain.Storage,
                    Priority.P2,
                    "Un volumen tiene menos de 10 % de espacio libre.",
                    "Revisa el uso del volumen y prepara una limpieza segura.",
                    datum);
            }
            else if (datum.NumericValue is < 20)
            {
                yield return Finding(
                    "STORAGE-FREE-WATCH",
                    HealthDomain.Storage,
                    Priority.P3,
                    "Un volumen tiene menos de 20 % de espacio libre.",
                    "Vigila el crecimiento y considera mantenimiento preventivo.",
                    datum);
            }
        }

        var whea = Metrics(data, "events.whea.error.count")
            .Where(datum => datum.Coverage != EvidenceCoverage.Absent)
            .Sum(datum => datum.NumericValue ?? 0);
        if (whea > 0)
        {
            yield return new DiagnosticFinding(
                "WHEA-RECENT",
                RuleSetVersion,
                HealthDomain.HardwareReliability,
                Priority.P2,
                RiskLevel.R0,
                "Se encontraron errores o advertencias WHEA recientes.",
                "Revisa los eventos y controladores antes de interpretar estabilidad.",
                $"events.whea.error.count={whea:0}");
        }

        if (Metrics(data, "events.whea.unknown.count").Any(datum => datum.NumericValue > 0))
        {
            yield return new DiagnosticFinding(
                "WHEA-UNCLASSIFIED", RuleSetVersion, HealthDomain.HardwareReliability,
                Priority.P3, RiskLevel.R0,
                "Hay registros WHEA cuya severidad no pudo interpretarse.",
                "Revisa esos registros; no se consideran evidencia de hardware saludable.",
                "events.whea.unknown.count>0");
        }

        if (Metrics(data, "system.reboot.pending").Any(datum => datum.TextValue == "true"))
        {
            yield return new DiagnosticFinding(
                "WINDOWS-REBOOT-PENDING",
                RuleSetVersion,
                HealthDomain.WindowsIntegrity,
                Priority.P3,
                RiskLevel.R3,
                "Windows tiene un reinicio pendiente.",
                "Guarda tu trabajo y programa el reinicio; WinMaint no reinicia automáticamente.",
                "system.reboot.pending=true");
        }
    }

    private static Dictionary<HealthDomain, int> CalculateConfidence(
        IReadOnlyList<DiagnosticDatum> data,
        StabilityTestResult? stability)
    {
        return new Dictionary<HealthDomain, int>
        {
            [HealthDomain.HardwareReliability] = AverageCoverage(
                data,
                "storage.disk.health",
                "storage.disk.operational",
                "events.whea.error.count"),
            [HealthDomain.Storage] = AverageCoverage(
                data,
                "storage.volume.free.percent",
                "storage.disk.health",
                "storage.disk.read-errors",
                "storage.disk.write-errors",
                "storage.disk.temperature.celsius",
                "storage.disk.wear.percent"),
            [HealthDomain.Memory] = MemoryEvidencePolicy.Evaluate(data).PhysicalCoveragePercent,
            [HealthDomain.WindowsIntegrity] = AverageCoverage(
                data,
                "windows.integrity.status",
                "events.error.count"),
            [HealthDomain.LoadStability] = StabilityConfidence(stability),
        };
    }

    private static int AverageCoverage(IReadOnlyList<DiagnosticDatum> data, params string[] keys)
    {
        var values = keys.Select(key =>
        {
            var matching = Metrics(data, key).ToArray();
            return matching.Length == 0 ? 0d : matching.Average(InterpretedCoverage);
        });
        return (int)Math.Round(values.Average() * 100d, MidpointRounding.AwayFromZero);
    }

    private static double InterpretedCoverage(DiagnosticDatum datum)
    {
        if (datum.MetricKey == "storage.disk.operational" &&
            DiskOperationalPolicy.Classify(datum.TextValue) == DiskOperationalState.Unknown) return 0;
        if (datum.MetricKey == "storage.disk.health" &&
            !new[] { "Healthy", "Warning", "Unhealthy" }.Contains(datum.TextValue, StringComparer.OrdinalIgnoreCase)) return 0;
        return CoverageFactor(datum.Coverage);
    }

    private static int StabilityConfidence(StabilityTestResult? stability)
    {
        if (stability is null || stability.Probes.Count == 0)
        {
            return 0;
        }

        if (stability.Probes.Any(probe => probe.ProbeId.StartsWith("hardware-events", StringComparison.Ordinal) &&
            probe.Status is not (StabilityProbeStatus.Passed or StabilityProbeStatus.Failed))) return 0;
        if (!RequiredWheaProbes.All(id => stability.Probes.Any(probe =>
            probe.ProbeId == id && probe.Status is StabilityProbeStatus.Passed or StabilityProbeStatus.Failed))) return 0;
        var intensive = stability.Probes.Where(probe => probe.ProbeId is "cpu" or "memory" or "storage-read")
            .DistinctBy(probe => probe.ProbeId).ToArray();
        var completed = intensive.Count(probe =>
            probe.Status is StabilityProbeStatus.Passed or StabilityProbeStatus.Failed);
        return (int)Math.Round(100d * completed / 3, MidpointRounding.AwayFromZero);
    }

    private static HealthDomainScore CreateDomainScore(HealthDomain domain, int confidence, int penalty)
    {
        var sufficient = confidence >= MinimumDomainConfidence;
        return new HealthDomainScore(
            domain,
            sufficient ? Math.Max(0, 100 - penalty) : null,
            confidence,
            sufficient);
    }

    private static int PenaltyFor(DiagnosticFinding finding) => finding.Priority switch
    {
        Priority.P0 => 100,
        Priority.P1 => 45,
        Priority.P2 => 20,
        Priority.P3 => 5,
        _ => 0,
    };

    private static string BandFor(int? score) => score switch
    {
        null => "Diagnóstico insuficiente",
        >= 90 => "Excelente",
        >= 75 => "Buena",
        >= 60 => "Requiere atención",
        >= 40 => "Mala",
        _ => "Crítica",
    };

    private static IEnumerable<DiagnosticDatum> Metrics(
        IEnumerable<DiagnosticDatum> data,
        string key) => data.Where(datum => datum.MetricKey == key);

    private static double CoverageFactor(EvidenceCoverage coverage) => coverage switch
    {
        EvidenceCoverage.Complete => 1d,
        EvidenceCoverage.Partial => 0.5d,
        _ => 0d,
    };

    private static DiagnosticFinding Finding(
        string ruleId,
        HealthDomain domain,
        Priority priority,
        string summary,
        string recommendation,
        DiagnosticDatum datum) => new(
            ruleId,
            RuleSetVersion,
            domain,
            priority,
            RiskLevel.R0,
            summary,
            recommendation,
            $"{datum.MetricKey}={datum.TextValue ?? datum.Value}; {datum.Source}");
}
