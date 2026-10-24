using WinMaint.Domain;

namespace WinMaint.Application;

public sealed record TemporaryCleanupDiagnosticEvidence(Guid SessionId, Guid InventoryId, DiagnosticReport Snapshot);

public static class TemporaryCleanupDiagnosticPolicy
{
    public const string Version = "1.0.0";

    public static bool IsSufficient(TemporaryCleanupPlan plan, DateTimeOffset now)
    {
        var evidence = plan.DiagnosticEvidence;
        if (!plan.HasDiagnostic || plan.SessionId == Guid.Empty || evidence is null ||
            evidence.SessionId != plan.SessionId || evidence.InventoryId != plan.Inventory.Id ||
            evidence.Snapshot is not { IsReadOnly: true, Sections: not null } snapshot ||
            snapshot.CapturedAt == default || snapshot.CapturedAt > plan.Inventory.CapturedAt ||
            now - snapshot.CapturedAt is var age && (age < TimeSpan.Zero || age > TimeSpan.FromMinutes(5)) ||
            snapshot.Sections.Any(section => section is null || section.Data is null || section.Data.Any(datum => datum is null)))
            return false;
        var data = snapshot.Sections.SelectMany(section => section.Data).ToArray();
        var compatibility = data.Where(datum => datum.MetricKey == "system.compatibility").ToArray();
        // Native root policy currently permits only local C:. Match our provider's canonical volume datum,
        // not an arbitrary healthy volume or a localized Windows command's text.
        var volume = data.Where(datum => datum.MetricKey == "storage.volume.free.percent" && datum.Name == @"Volumen C:\").ToArray();
        if (compatibility.Length != 1 || compatibility[0].Coverage is not (EvidenceCoverage.Complete or EvidenceCoverage.Partial) ||
            compatibility[0].TextValue != "supported" || volume.Length != 1 || volume[0].Coverage is not (EvidenceCoverage.Complete or EvidenceCoverage.Partial) ||
            volume[0].NumericValue is not double free || !double.IsFinite(free) || free is < 0 or > 100)
            return false;
        var health = HealthRuleEngine.Evaluate(snapshot);
        return !health.HasPriorityZero && !health.Findings.Any(finding =>
            finding.RuleId == "MEMORY-DIAGNOSTIC-FAILED" ||
            finding.Domain == HealthDomain.HardwareReliability && finding.Priority <= Priority.P1);
    }
}
