using WinMaint.Application;
using WinMaint.Domain;

namespace WinMaint.Application.Tests;

public sealed class HealthRuleEngineTests
{
    [Fact]
    public void PriorityZeroCapsOverallScoreAndBlocksPreflight()
    {
        var report = Report(
            Datum("storage.disk.health", text: "Unhealthy"),
            Datum("storage.disk.operational", text: "OK"),
            Datum("storage.volume.free.percent", number: 60),
            Datum("events.whea.error.count", number: 0, coverage: EvidenceCoverage.Partial),
            Datum("performance.cpu.percent", number: 10, coverage: EvidenceCoverage.Partial),
            Datum("memory.available.bytes", number: 8L * 1024 * 1024 * 1024),
            Datum("memory.load.percent", number: 20),
            Datum("power.source", text: "Corriente alterna"));

        var assessment = HealthRuleEngine.Evaluate(report);
        var preflight = StabilityPreflightEvaluator.Evaluate(report, assessment);

        Assert.True(assessment.HasPriorityZero);
        Assert.True(assessment.OverallScore <= 39);
        Assert.False(preflight.CanRun);
        Assert.Contains(preflight.Checks, check => check.Id == "priority-zero" && check.Status == PreflightStatus.Block);
    }

    [Fact]
    public void MissingEvidenceLowersConfidenceWithoutHealthPenalty()
    {
        var report = Report(
            Datum("storage.disk.health", text: "Healthy"),
            Datum("storage.disk.operational", text: "OK"),
            DiagnosticDatum.Unknown("Temperatura", "fixture", "No disponible", "storage.disk.temperature.celsius"),
            Datum("storage.volume.free.percent", number: 50),
            Datum("events.whea.error.count", number: 0, coverage: EvidenceCoverage.Partial),
            Datum("memory.available.bytes", number: 4L * 1024 * 1024 * 1024),
            Datum("memory.load.percent", number: 30));

        var assessment = HealthRuleEngine.Evaluate(report);

        Assert.Empty(assessment.Findings);
        Assert.Equal(100, assessment.OverallScore);
        Assert.InRange(assessment.ConfidencePercent, 1, 99);
    }

    [Fact]
    public void PriorityOneCapsOverallScoreAtFiftyNine()
    {
        var report = Report(
            Datum("storage.disk.health", text: "Healthy"),
            Datum("storage.disk.operational", text: "OK"),
            Datum("storage.volume.free.percent", number: 4),
            Datum("events.whea.error.count", number: 0, coverage: EvidenceCoverage.Partial),
            Datum("memory.available.bytes", number: 4L * 1024 * 1024 * 1024),
            Datum("memory.load.percent", number: 30));

        var assessment = HealthRuleEngine.Evaluate(report);

        Assert.Contains(assessment.Findings, finding => finding.Priority == Priority.P1);
        Assert.True(assessment.OverallScore <= 59);
    }

    private static DiagnosticReport Report(params DiagnosticDatum[] data) => new(
        DateTimeOffset.UtcNow,
        [new DiagnosticSection("fixture", "Fixture", EvidenceCoverage.Partial, data, [])],
        IsReadOnly: true);

    [Theory]
    [InlineData(0, 4, false)]
    [InlineData(1, 4, true)]
    public void OnlyClassifiedWheaErrorsPenalizeHardware(double errors, double informational, bool hasFinding)
    {
        var assessment = HealthRuleEngine.Evaluate(Report(
            Datum("events.whea.count", number: errors + informational),
            Datum("events.whea.error.count", number: errors),
            Datum("events.whea.informational.count", number: informational),
            Datum("storage.disk.health", text: "Healthy"),
            Datum("storage.disk.operational", text: "OK")));
        Assert.Equal(hasFinding, assessment.Findings.Any(finding => finding.RuleId == "WHEA-RECENT"));
        Assert.Equal(hasFinding ? 80 : 100,
            assessment.Domains.Single(domain => domain.Domain == HealthDomain.HardwareReliability).Score);
        Assert.Equal("1.0.4", assessment.RuleSetVersion);
    }

    [Fact]
    public void UnknownWheaIsVisibleAndPreflightDoesNotCallItHealthy()
    {
        var report = Report(Datum("events.whea.error.count", number: 0),
            Datum("events.whea.unknown.count", number: 1));
        var assessment = HealthRuleEngine.Evaluate(report);
        Assert.Contains(assessment.Findings, finding => finding.RuleId == "WHEA-UNCLASSIFIED");
        Assert.Contains(StabilityPreflightEvaluator.Evaluate(report, assessment).Checks,
            check => check.Id == "whea" && check.Status == PreflightStatus.Unknown);
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
}
