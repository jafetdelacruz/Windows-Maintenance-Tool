using System.Text.Json;
using WinMaint.Application;
using WinMaint.Domain;

namespace WinMaint.Reporting;

public sealed record RequiredReportMetric(string ProviderId, string MetricKey, string? DatumName = null);
public sealed record ReadOnlyReportRequirements(string ExactToolVersion, int SnapshotCount,
    int InventoryCount, bool RequireNonEmptyInventory, IReadOnlyList<RequiredReportMetric> RequiredMetrics,
    string ExactRuleSetVersion = HealthRuleEngine.RuleSetVersion, bool RequireEmptyInventory = false);
public sealed record ReadOnlyReportAcceptance(bool Accepted, IReadOnlyList<string> Failures);

// Acceptance of a new R0 scenario, not a migration or reinterpretation of historical health.
public static class ReadOnlyReportAcceptanceVerifier
{
    public static ReadOnlyReportAcceptance Verify(string json, ReadOnlyReportRequirements requirements)
    {
        ArgumentNullException.ThrowIfNull(requirements);
        if (string.IsNullOrWhiteSpace(requirements.ExactToolVersion) || requirements.ExactRuleSetVersion != HealthRuleEngine.RuleSetVersion ||
            requirements.SnapshotCount <= 0 ||
            requirements.InventoryCount < 0 || requirements.RequiredMetrics is not { Count: > 0 } ||
            requirements.RequiredMetrics.Any(metric => metric is null || string.IsNullOrWhiteSpace(metric.ProviderId) ||
                string.IsNullOrWhiteSpace(metric.MetricKey) || metric.DatumName is not null && string.IsNullOrWhiteSpace(metric.DatumName)) ||
            requirements.RequiredMetrics.Distinct().Count() != requirements.RequiredMetrics.Count ||
            (requirements.RequireNonEmptyInventory || requirements.RequireEmptyInventory) && requirements.InventoryCount == 0 ||
            requirements.RequireNonEmptyInventory && requirements.RequireEmptyInventory)
            return new(false, ["invalid-acceptance-requirements"]);
        SessionReport report;
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
            try { EvidenceJsonValidation.NoDuplicates(document.RootElement); report = ReportRenderer.Parse(json); }
            catch { document.Dispose(); throw; }
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        { return new(false, ["invalid-report-schema-or-content"]); }
        using (document)
        {
            var failures = new List<string>();
            if (report.SchemaVersion != "1.2" || report.ToolVersion != requirements.ExactToolVersion)
                failures.Add("wrong-schema-or-tool-version");
            if (report.SessionId == Guid.Empty || report.ExportId is null || report.ExportId == Guid.Empty ||
                report.StartedAt == default || report.UpdatedAt < report.StartedAt || report.Status != "Completed")
                failures.Add("invalid-session-or-export-identity");
            if (report.Snapshots.Count != requirements.SnapshotCount || report.FinalHealth is null)
                failures.Add("missing-diagnostic-or-final-assessment");
            if (report.FinalHealth?.RuleSetVersion != requirements.ExactRuleSetVersion ||
                report.Snapshots.Any(snapshot => snapshot.Health.RuleSetVersion != requirements.ExactRuleSetVersion))
                failures.Add("wrong-health-rule-version");
            if (report.Actions.Count != 0 || report.StabilityTests.Count != 0 || report.Decisions.Count != 0)
                failures.Add("unexpected-non-diagnostic-activity");
            if (report.TemporaryCleanup.Count != requirements.InventoryCount ||
                report.TemporaryCleanup.Any(record => record.Status != "InventoryOnly" || record.Files.Count != 0 ||
                    record.JournalAttemptId is not null || record.Inventory.Coverage != EvidenceCoverage.Complete) ||
                report.TemporaryCleanup.Select(record => record.Inventory.Id).Distinct().Count() != report.TemporaryCleanup.Count)
                failures.Add("wrong-inventory-count-identity-or-mutable-activity");
            if (requirements.RequireNonEmptyInventory && report.TemporaryCleanup.Any(record =>
                record.Inventory.Coverage != EvidenceCoverage.Complete || record.Inventory.CandidateFiles is null or <= 0))
                failures.Add("nonempty-complete-inventory-not-exercised");
            if (requirements.RequireEmptyInventory && report.TemporaryCleanup.Any(record =>
                record.Inventory.CandidateFiles != 0 || record.Inventory.CandidateLogicalBytes != 0))
                failures.Add("empty-complete-inventory-not-exercised");

            foreach (var snapshot in report.Snapshots)
            {
                if (!snapshot.Diagnostic.IsReadOnly || snapshot.Diagnostic.CapturedAt == default ||
                    snapshot.Diagnostic.CapturedAt > report.UpdatedAt ||
                    snapshot.Diagnostic.Sections.Any(section => string.IsNullOrWhiteSpace(section.Id) || !Enum.IsDefined(section.Coverage)) ||
                    snapshot.Diagnostic.Sections.Select(section => section.Id).Distinct(StringComparer.Ordinal).Count() != snapshot.Diagnostic.Sections.Count)
                    failures.Add("invalid-diagnostic-identity-or-mode");
                if (snapshot.Diagnostic.Sections.SelectMany(section => section.Data).Any(datum =>
                    !Enum.IsDefined(datum.Coverage) || string.IsNullOrWhiteSpace(datum.Name) ||
                    string.IsNullOrWhiteSpace(datum.Value) || string.IsNullOrWhiteSpace(datum.Source) ||
                    datum.NumericValue is double value && !double.IsFinite(value)))
                    failures.Add("invalid-diagnostic-datum");
                foreach (var metric in requirements.RequiredMetrics)
                {
                    var matches = snapshot.Diagnostic.Sections.Where(section => section.Id == metric.ProviderId &&
                            section.Coverage != EvidenceCoverage.Absent)
                        .SelectMany(section => section.Data).Where(datum => datum.MetricKey == metric.MetricKey &&
                            (metric.DatumName is null || datum.Name == metric.DatumName)).ToArray();
                    if (matches.Length != 1 || !AvailableAndMeaningful(matches[0]))
                        failures.Add("required-functional-evidence-unavailable:" + metric.ProviderId + "/" + metric.MetricKey);
                }
            }
            // Re-evaluation is limited to a new candidate using this exact rule version, never legacy readers.
            if (!failures.Contains("wrong-health-rule-version", StringComparer.Ordinal) &&
                !failures.Contains("invalid-diagnostic-datum", StringComparer.Ordinal))
            {
                var calculated = report.Snapshots.Select(snapshot => HealthRuleEngine.Evaluate(snapshot.Diagnostic)).ToArray();
                if (report.Snapshots.Where((snapshot, index) => !SameHealth(snapshot.Health, calculated[index])).Any() ||
                    !SameHealth(report.FinalHealth, HealthRuleEngine.EvaluateSession(calculated, [])))
                    failures.Add("health-assessment-inconsistent-with-current-evidence");
            }
            // Computed counters are mandatory for this new acceptance profile, even though old readers tolerate omission.
            if (document.RootElement.TryGetProperty("temporaryCleanup", out var cleanup) && cleanup.ValueKind == JsonValueKind.Array)
                foreach (var raw in cleanup.EnumerateArray())
                    if (!raw.TryGetProperty("deletedFiles", out _) || !raw.TryGetProperty("deletedLogicalBytes", out _) ||
                        !raw.TryGetProperty("physicalSpaceFreed", out _)) failures.Add("missing-explicit-cleanup-counters");
            return new(failures.Count == 0, failures.Distinct(StringComparer.Ordinal).ToArray());
        }
    }

    private static bool SameHealth(HealthAssessment? left, HealthAssessment? right) =>
        JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right);

    private static bool AvailableAndMeaningful(DiagnosticDatum datum)
    {
        if (datum.Coverage == EvidenceCoverage.Absent || datum.Value == DiagnosticDatum.UnknownValue) return false;
        if (datum.MetricKey == "system.compatibility") return datum.TextValue == "supported";
        if (datum.MetricKey?.EndsWith(".percent", StringComparison.Ordinal) == true)
            return datum.NumericValue is double value && double.IsFinite(value) && value is >= 0 and <= 100;
        if (datum.MetricKey?.EndsWith(".bytes", StringComparison.Ordinal) == true)
            return datum.NumericValue is double value && double.IsFinite(value) && value >= 0;
        if (datum.MetricKey?.EndsWith(".count", StringComparison.Ordinal) == true)
            return datum.NumericValue is double value && double.IsFinite(value) && value >= 0 && Math.Truncate(value) == value;
        return datum.NumericValue is double numeric && double.IsFinite(numeric) ||
            !string.IsNullOrWhiteSpace(datum.TextValue) && datum.TextValue != DiagnosticDatum.UnknownValue;
    }
}
