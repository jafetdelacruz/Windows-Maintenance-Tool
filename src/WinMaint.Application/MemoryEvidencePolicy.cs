using WinMaint.Domain;

namespace WinMaint.Application;

public static class MemoryEvidencePolicy
{
    // Contextual presentation threshold, not a physical-health or sustained-pressure rule.
    public const double HighCurrentUsePercent = 90;

    public static MemoryEvidenceAssessment Evaluate(IReadOnlyList<DiagnosticDatum> data,
        StabilityTestResult? stability = null)
    {
        var usage = data.Where(datum => datum.MetricKey == "memory.load.percent").ToArray();
        double? load = usage.Length == 1 && usage[0].Coverage != EvidenceCoverage.Absent &&
            usage[0].NumericValue is double number && double.IsFinite(number) && number is >= 0 and <= 100 ? number : null;
        var previous = data.Where(datum => datum.MetricKey == "memory.diagnostic.previous.status").ToArray();
        var status = previous.Length == 1 && previous[0].Coverage != EvidenceCoverage.Absent &&
            previous[0].TextValue is "passed" or "failed" ? previous[0].TextValue! : "unknown";
        // A historical software diagnostic is partial evidence, never full physical coverage.
        var coverage = status == "unknown" ? 0 : 50;
        var state = status switch
        {
            "passed" => MemoryPhysicalState.NoFaultsObservedInPreviousTest,
            "failed" => MemoryPhysicalState.PreviousFailureRequiresReview,
            _ => MemoryPhysicalState.InsufficientEvidence,
        };
        var probes = stability?.Probes.Where(probe => probe.ProbeId == "memory").ToArray();
        var pattern = probes is { Length: 1 } ? probes[0].Status.ToString() : "NotRun";
        // Process memory samples belong to load stability; they do not certify every physical DIMM.
        return new(load, load.HasValue ? load >= HighCurrentUsePercent : null,
            EvidenceCoverage.Absent, state, coverage, status, pattern);
    }
}
