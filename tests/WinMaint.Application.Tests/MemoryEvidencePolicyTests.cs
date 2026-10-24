using System.Text.Json;
using System.Text.Json.Nodes;
using WinMaint.Domain;
using WinMaint.Reporting;

namespace WinMaint.Application.Tests;

public sealed class MemoryEvidencePolicyTests
{
    [Theory]
    [InlineData(20, false)]
    [InlineData(89.9, false)]
    [InlineData(90, true)]
    [InlineData(100, true)]
    public void InstantaneousUsageIsContextNotSustainedPressureOrPhysicalHealth(double load, bool high)
    {
        var assessment = HealthRuleEngine.Evaluate(Report(Usage(load), Metric("memory.available.bytes", 8e9),
            Metric("events.whea.error.count", 0)));
        var memory = assessment.MemoryEvidence!;
        Assert.Equal(high, memory.HighUseInCurrentSample);
        Assert.Equal(EvidenceCoverage.Absent, memory.SustainedPressureCoverage);
        Assert.Equal(MemoryPhysicalState.InsufficientEvidence, memory.PhysicalState);
        Assert.Equal(0, memory.PhysicalCoveragePercent);
        var score = Assert.Single(assessment.Domains, domain => domain.Domain == HealthDomain.Memory);
        Assert.Null(score.Score);
        Assert.Equal(0, score.ConfidencePercent);
        Assert.Empty(assessment.Findings);
        Assert.Contains("presión sostenida no medida", memory.Summary);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1)]
    [InlineData(101)]
    public void InvalidUsageRemainsUnknown(double load)
    {
        var memory = MemoryEvidencePolicy.Evaluate([Usage(load)]);
        Assert.Null(memory.CurrentLoadPercent);
        Assert.Null(memory.HighUseInCurrentSample);
    }

    [Theory]
    [InlineData("passed", MemoryPhysicalState.NoFaultsObservedInPreviousTest, 100)]
    [InlineData("failed", MemoryPhysicalState.PreviousFailureRequiresReview, 55)]
    public void PriorDiagnosticProvidesOnlyPartialPhysicalEvidence(string result, MemoryPhysicalState state, int expected)
    {
        var report = Report(Previous(result), Usage(95));
        var assessment = HealthRuleEngine.Evaluate(report);
        Assert.Equal(state, assessment.MemoryEvidence!.PhysicalState);
        var memory = Assert.Single(assessment.Domains, domain => domain.Domain == HealthDomain.Memory);
        Assert.Equal(50, memory.ConfidencePercent);
        Assert.Equal(expected, memory.Score);
        if (result == "failed")
        {
            var finding = Assert.Single(assessment.Findings);
            Assert.Equal("MEMORY-DIAGNOSTIC-FAILED", finding.RuleId);
            Assert.Equal(Priority.P1, finding.Priority);
            Assert.True(assessment.OverallScore <= 59);
        }
        else Assert.Empty(assessment.Findings);
        Assert.Equal(EvidenceCoverage.Absent, assessment.MemoryEvidence.SustainedPressureCoverage);
    }

    [Fact]
    public void AmbiguousPreviousResultsCannotProvidePhysicalCoverage()
    {
        Assert.Equal(0, MemoryEvidencePolicy.Evaluate([Previous("passed"), Previous("failed")]).PhysicalCoveragePercent);
        Assert.Equal(0, MemoryEvidencePolicy.Evaluate([Previous("unknown")]).PhysicalCoveragePercent);
        Assert.Equal(0, MemoryEvidencePolicy.Evaluate([Previous("passed") with { Coverage = EvidenceCoverage.Absent }]).PhysicalCoveragePercent);
    }

    [Theory]
    [InlineData("failed", false)]
    [InlineData("passed", true)]
    [InlineData("unknown", true)]
    public void KnownPreviousFailureBlocksLoadButAbsentPhysicalDiagnosticIsNotUniversalRequirement(string status, bool allowed)
    {
        var report = Report(Previous(status), Usage(30), Metric("memory.available.bytes", 4e9),
            Metric("storage.volume.free.percent", 50), Metric("performance.cpu.percent", 10),
            Metric("events.whea.error.count", 0),
            DiagnosticDatum.Available("Power", "AC", "Fixture", metricKey: "power.source", textValue: "Corriente alterna"),
            DiagnosticDatum.Available("Disk", "Healthy", "Fixture", metricKey: "storage.disk.health", textValue: "Healthy"),
            DiagnosticDatum.Available("Operational", "OK", "Fixture", metricKey: "storage.disk.operational", textValue: "OK"));
        var preflight = StabilityPreflightEvaluator.Evaluate(report, HealthRuleEngine.Evaluate(report));
        Assert.Equal(allowed, preflight.CanRun);
        Assert.Equal(!allowed, preflight.Checks.Any(check => check.Id == "memory-prior-failure" && check.Status == PreflightStatus.Block));
    }

    [Theory]
    [InlineData(StabilityProbeStatus.Passed)]
    [InlineData(StabilityProbeStatus.Failed)]
    [InlineData(StabilityProbeStatus.Cancelled)]
    [InlineData(StabilityProbeStatus.Skipped)]
    public void ProcessPatternResultDoesNotCertifyAllPhysicalRam(StabilityProbeStatus status)
    {
        var test = new StabilityTestResult(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            new([], true), [new("memory", "Fixture only", status, TimeSpan.Zero, "Synthetic", new Dictionary<string, double>())],
            false, false, []);
        var memory = MemoryEvidencePolicy.Evaluate([Usage(50)], test);
        Assert.Equal(status.ToString(), memory.ProcessPatternStatus);
        Assert.Equal(0, memory.PhysicalCoveragePercent);
        Assert.Equal(MemoryPhysicalState.InsufficientEvidence, memory.PhysicalState);
    }

    [Fact]
    public void LaterUsageOrPassCannotResolveRetainedPriorFailureAndHistoryIsNotReevaluated()
    {
        var failed = HealthRuleEngine.Evaluate(Report(Previous("failed")));
        var passed = HealthRuleEngine.Evaluate(Report(Previous("passed")));
        var final = HealthRuleEngine.EvaluateSession([failed, passed], [])!;
        Assert.Equal(MemoryPhysicalState.PreviousFailureRequiresReview, final.MemoryEvidence!.PhysicalState);
        Assert.Contains(final.Findings, finding => finding.RuleId == "MEMORY-DIAGNOSTIC-FAILED");
        Assert.True(final.OverallScore <= 59);
        var historical = passed with { RuleSetVersion = "historical-rules", MemoryEvidence = null };
        Assert.Null(HealthRuleEngine.EvaluateSession([historical], [])!.MemoryEvidence);
        Assert.Equal("historical-rules", historical.RuleSetVersion);
    }

    [Fact]
    public void JsonAndTextDescribeInsufficientPhysicalEvidenceDespiteCompleteUsageMetrics()
    {
        var journal = new ReportJournal("unused-no-io");
        journal.AddSnapshot(Report(Usage(95), Metric("memory.available.bytes", 1e9), Metric("events.whea.error.count", 0)));
        var saved = ReportRenderer.Parse(ReportRenderer.ToJson(journal.CreateReport("Completed")));
        Assert.Equal("1.0.4", saved.FinalHealth!.RuleSetVersion);
        Assert.Equal(0, saved.FinalHealth.MemoryEvidence!.PhysicalCoveragePercent);
        var text = ReportRenderer.ToText(saved);
        Assert.Contains("uso alto en esta muestra", text);
        Assert.Contains("presión sostenida no medida", text);
        Assert.Contains("diagnóstico físico insuficiente", text);
    }

    [Theory]
    [InlineData("percent")]
    [InlineData("usage-label")]
    [InlineData("sustained")]
    [InlineData("physical-coverage")]
    [InlineData("physical-state")]
    [InlineData("pattern")]
    public void ContradictoryMemoryEvidenceIsRejected(string corruption)
    {
        var journal = new ReportJournal("unused-no-io");
        journal.AddSnapshot(Report(Usage(95)));
        var root = JsonNode.Parse(ReportRenderer.ToJson(journal.CreateReport("Completed")))!;
        var memory = root["finalHealth"]!["memoryEvidence"]!;
        switch (corruption)
        {
            case "percent": memory["currentLoadPercent"] = 101; break;
            case "usage-label": memory["highUseInCurrentSample"] = false; break;
            case "sustained": memory["sustainedPressureCoverage"] = "Complete"; break;
            case "physical-coverage": memory["physicalCoveragePercent"] = 100; break;
            case "physical-state": memory["physicalState"] = 999; break;
            case "pattern": memory["processPatternStatus"] = "Certified"; break;
        }
        Assert.Throws<JsonException>(() => ReportRenderer.Parse(root.ToJsonString()));
    }

    private static DiagnosticDatum Usage(double value) => Metric("memory.load.percent", value);
    private static DiagnosticDatum Metric(string key, double value) => DiagnosticDatum.Available(key, "Fixture", "Fixture",
        metricKey: key, numericValue: value);
    private static DiagnosticDatum Previous(string status) => DiagnosticDatum.Available("Prior test", status, "Fixture",
        EvidenceCoverage.Partial, metricKey: "memory.diagnostic.previous.status", textValue: status);
    private static DiagnosticReport Report(params DiagnosticDatum[] data) => new(DateTimeOffset.UtcNow,
        [new("fixture", "Fixture", EvidenceCoverage.Partial, data, [])], true);
}
