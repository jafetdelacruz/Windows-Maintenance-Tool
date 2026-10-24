using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using WinMaint.Application;
using WinMaint.Domain;
using WinMaint.Reporting;

namespace WinMaint.ReviewRegression.Tests;

// Regression coverage from the independent review. Probes and stores are in-memory fakes.
public sealed class AstraPriorityRegressionTests
{
    [Theory]
    [InlineData(Priority.P0)]
    [InlineData(Priority.P1)]
    [Trait("Finding", "H01")]
    public void SavedSummaryMustMatchHealthEngineAfterStabilityFailure(Priority priority)
    {
        var baseline = SafeReport();
        var stability = StabilityFailure(priority);
        var journal = new ReportJournal("unused-no-io");
        journal.AddSnapshot(baseline);
        journal.AddStability(stability);
        var saved = ReportRenderer.Parse(ReportRenderer.ToJson(journal.CreateReport("Completed")));

        Assert.Equal(HealthRuleEngine.Evaluate(baseline, stability).OverallScore, SummaryScore(saved));
    }

    [Fact]
    [Trait("Finding", "H01")]
    public void AnotherDiagnosticDoesNotResolveAnUnresolvedStabilityFailure()
    {
        var journal = new ReportJournal("unused-no-io");
        journal.AddSnapshot(SafeReport());
        journal.AddStability(StabilityFailure(Priority.P0));
        journal.AddSnapshot(SafeReport());

        Assert.InRange(SummaryScore(journal.CreateReport("Completed")), 0, 39);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Finding", "H01-control")]
    public void CancellationOrMissingStabilityDoesNotInventHardwareFailure(bool interrupted)
    {
        var journal = new ReportJournal("unused-no-io");
        journal.AddSnapshot(SafeReport());
        if (interrupted)
        {
            journal.AddStability(new(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
                new([], true), [new("cpu", "CPU", StabilityProbeStatus.Cancelled, TimeSpan.Zero,
                    "Synthetic cancellation", new Dictionary<string, double>())], true, false, []));
        }

        Assert.Equal(100, SummaryScore(journal.CreateReport("Partial")));
    }

    [Theory]
    [InlineData("OK, Predictive Failure")]
    [InlineData("Healthy, Error")]
    [Trait("Finding", "H02")]
    public void AHealthyStateMustNotHideAnAdverseState(string status)
    {
        var report = WithOperationalStatus(status);
        var assessment = HealthRuleEngine.Evaluate(report);
        Assert.Contains(assessment.Findings, finding => finding.RuleId == "STORAGE-OPERATIONAL-NOT-OK" && finding.Priority == Priority.P0);
        Assert.False(StabilityPreflightEvaluator.Evaluate(report, assessment).CanRun);
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("Unrecognized")]
    [Trait("Finding", "H02")]
    public void AnUnrecognizedStateIsNotAutomaticallyAPriorityZeroFailure(string status)
    {
        Assert.DoesNotContain(HealthRuleEngine.Evaluate(WithOperationalStatus(status)).Findings,
            finding => finding.RuleId == "STORAGE-OPERATIONAL-NOT-OK" && finding.Priority == Priority.P0);
    }

    [Theory]
    [InlineData("OK")]
    [InlineData("Healthy")]
    [Trait("Finding", "H02-control")]
    public void ExactNormalStatesDoNotInventAnOperationalFault(string status)
    {
        Assert.DoesNotContain(HealthRuleEngine.Evaluate(WithOperationalStatus(status)).Findings,
            finding => finding.RuleId == "STORAGE-OPERATIONAL-NOT-OK");
    }

    [Fact]
    [Trait("Finding", "H02-control")]
    public void AStandaloneAdverseStateRemainsVisible()
    {
        Assert.Contains(HealthRuleEngine.Evaluate(WithOperationalStatus("Predictive Failure")).Findings,
            finding => finding.RuleId == "STORAGE-OPERATIONAL-NOT-OK" && finding.Priority == Priority.P0);
    }

    [Theory]
    [InlineData("power.source")]
    [InlineData("storage.volume.free.percent")]
    [InlineData("memory.available.bytes")]
    [InlineData("events.whea.error.count")]
    [InlineData("all")]
    [Trait("Finding", "H03")]
    public async Task MissingRequiredEvidenceMustNotInvokeAnyProbe(string missing)
    {
        var data = missing == "all" ? [] : SafeData().Where(datum => datum.MetricKey != missing).ToArray();
        await AssertNoProbeStartsAsync(Report(data));
    }

    [Theory]
    [InlineData("memory.available.bytes")]
    [InlineData("storage.volume.free.percent")]
    [Trait("Finding", "H03")]
    public async Task NonFiniteRequiredEvidenceMustNotInvokeAnyProbe(string invalid)
    {
        var data = SafeData().Select(datum => datum.MetricKey == invalid
            ? datum with { NumericValue = double.NaN, Value = "NaN" } : datum).ToArray();
        await AssertNoProbeStartsAsync(Report(data));
    }

    [Fact]
    [Trait("Finding", "H03-control")]
    public async Task SufficientSyntheticEvidenceCanInvokeTheFakeProbe()
    {
        var report = SafeReport();
        var probe = new RecordingProbe();
        var store = new InMemoryStore();
        var coordinator = new StabilityTestCoordinator([probe], store, ZeroDurations(), new SafeSafetySource(report));

        await coordinator.RunAsync(report, HealthRuleEngine.Evaluate(report));

        Assert.Equal(1, probe.Calls);
        Assert.True(store.Started);
    }

    [Theory]
    [InlineData(Priority.P0)]
    [InlineData(Priority.P1)]
    public void FinalAssessmentIsSharedByJournalJsonAndTextWithoutChangingSnapshots(Priority priority)
    {
        var journal = new ReportJournal("unused-no-io");
        journal.AddSnapshot(SafeReport());
        var before = journal.CreateReport("Partial").Snapshots[0];
        journal.AddStability(StabilityFailure(priority));
        journal.AddSnapshot(SafeReport());
        var report = journal.CreateReport("Completed");
        var parsed = ReportRenderer.Parse(ReportRenderer.ToJson(report));
        Assert.Equal("1.2", parsed.SchemaVersion);
        Assert.Equal(before, report.Snapshots[0]);
        Assert.Equal(100, parsed.Snapshots[0].Health.OverallScore);
        Assert.Equal(journal.FinalHealth!.OverallScore, parsed.FinalHealth!.OverallScore);
        Assert.Equal(parsed.FinalHealth.OverallScore, SummaryScore(parsed));
        Assert.DoesNotContain("Sin problemas urgentes", ReportRenderer.ToText(parsed));
        Assert.Contains(parsed.FinalHealth.Findings, finding => finding.Priority == priority);
    }

    [Fact]
    public void CancellationAndNewDiagnosticDoNotResolveEarlierFailureOrInventCoverage()
    {
        var journal = new ReportJournal("unused-no-io");
        journal.AddSnapshot(SafeReport());
        journal.AddStability(StabilityFailure(Priority.P0));
        journal.AddStability(StabilityFailure(Priority.P0) with
        {
            Probes = [new("memory", "Cancelled", StabilityProbeStatus.Cancelled, TimeSpan.Zero,
                "in-memory", new Dictionary<string, double>())],
            WasInterrupted = true,
            Findings = [],
        });
        journal.AddSnapshot(SafeReport());
        Assert.InRange(journal.FinalHealth!.OverallScore!.Value, 0, 39);
        Assert.Equal(0, journal.FinalHealth.Domains.Single(domain => domain.Domain == HealthDomain.LoadStability).ConfidencePercent);
        Assert.False(StabilityPreflightEvaluator.Evaluate(SafeReport(), journal.FinalHealth).CanRun);
    }

    [Fact]
    public void MissingDiagnosticEvidenceDoesNotInventScoreEvenWithFailure()
    {
        var journal = new ReportJournal("unused-no-io");
        journal.AddSnapshot(Report([]));
        journal.AddStability(StabilityFailure(Priority.P0) with { Probes = [] });
        var parsed = ReportRenderer.Parse(ReportRenderer.ToJson(journal.CreateReport("Partial")));
        Assert.Null(parsed.FinalHealth!.OverallScore);
        Assert.True(parsed.FinalHealth.HasPriorityZero);
        Assert.DoesNotContain("Evaluación orientativa:", ReportRenderer.ToText(parsed));
        Assert.DoesNotContain("Sin problemas urgentes", ReportRenderer.ToText(parsed));
    }

    [Fact]
    public void DiagnosticFaultIsNotImplicitlyResolvedByCleanSnapshot()
    {
        var journal = new ReportJournal("unused-no-io");
        journal.AddSnapshot(WithOperationalStatus("Error"));
        journal.AddSnapshot(SafeReport());
        Assert.True(journal.FinalHealth!.HasPriorityZero);
        Assert.InRange(SummaryScore(journal.CreateReport("Completed")), 0, 39);
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData("1.1")]
    public void LegacyReportsPreserveStoredRuleAndScoreAndDoNotReinterpretOldEvidence(string schema)
    {
        var journal = new ReportJournal("unused-no-io");
        journal.AddSnapshot(WithOperationalStatus("Unknown"));
        var snapshot = journal.CreateReport("Partial").Snapshots[0];
        snapshot = snapshot with
        {
            Health = snapshot.Health with
            { RuleSetVersion = "historical-rule", OverallScore = 72, Findings = [] }
        };
        var legacy = journal.CreateReport("Partial") with
        { SchemaVersion = schema, Snapshots = [snapshot], FinalHealth = null };
        var parsed = ReportRenderer.Parse(ReportRenderer.ToJson(legacy));
        Assert.Equal("historical-rule", parsed.Snapshots[0].Health.RuleSetVersion);
        Assert.Equal(72, SummaryScore(parsed));
        Assert.Null(parsed.FinalHealth);
        var failed = parsed with { StabilityTests = [StabilityFailure(Priority.P0)] };
        Assert.Equal(39, SummaryScore(failed));
        Assert.Equal(72, failed.Snapshots[0].Health.OverallScore);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("null-findings")]
    [InlineData("out-of-range")]
    public void SchemaTwelveRequiresValidExplicitFinalAssessment(string corruption)
    {
        var journal = new ReportJournal("unused-no-io");
        journal.AddSnapshot(SafeReport());
        var node = JsonNode.Parse(ReportRenderer.ToJson(journal.CreateReport("Partial")))!;
        if (corruption == "missing") node.AsObject().Remove("finalHealth");
        if (corruption == "null") node["finalHealth"] = null;
        if (corruption == "null-findings") node["finalHealth"]!["findings"] = null;
        if (corruption == "out-of-range") node["finalHealth"]!["overallScore"] = 101;
        Assert.Throws<JsonException>(() => ReportRenderer.Parse(node.ToJsonString()));
    }

    [Fact]
    public void EmptySessionHasExplicitNullFinalAssessment()
    {
        var report = new ReportJournal("unused-no-io").CreateReport("Partial");
        Assert.Null(ReportRenderer.Parse(ReportRenderer.ToJson(report)).FinalHealth);
        Assert.DoesNotContain("Evaluación orientativa:", ReportRenderer.ToText(report));
    }

    [Fact]
    public void FinalAssessmentCannotSilentlyDropRecordedFaults()
    {
        var journal = new ReportJournal("unused-no-io");
        journal.AddSnapshot(SafeReport());
        var healthy = journal.FinalHealth;
        journal.AddStability(StabilityFailure(Priority.P0));
        var inconsistent = journal.CreateReport("Partial") with { FinalHealth = healthy };
        Assert.Throws<JsonException>(() => ReportRenderer.Parse(ReportRenderer.ToJson(inconsistent)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Unknown")]
    [InlineData("Starting")]
    [InlineData("Degraded")]
    [InlineData("LOOKING HEALTHY")]
    [InlineData("NOT OK")]
    [InlineData("OK, Unknown")]
    [InlineData("OK,")]
    public async Task NonNormalStateBlocksLoadWithoutInventingPriorityZeroAndLowersCoverage(string status)
    {
        var report = WithOperationalStatus(status);
        var assessment = HealthRuleEngine.Evaluate(report);
        Assert.False(assessment.HasPriorityZero);
        Assert.True(assessment.ConfidencePercent < HealthRuleEngine.Evaluate(SafeReport()).ConfidencePercent);
        await AssertNoProbeStartsAsync(report);
    }

    [Theory]
    [InlineData("performance.cpu.percent")]
    [InlineData("storage.disk.operational")]
    [InlineData("storage.disk.health")]
    public async Task OtherMandatoryEvidenceCannotBeMissing(string missing) =>
        await AssertNoProbeStartsAsync(Report(SafeData().Where(datum => datum.MetricKey != missing).ToArray()));

    [Theory]
    [InlineData("power.source")]
    [InlineData("storage.volume.free.percent")]
    [InlineData("memory.available.bytes")]
    [InlineData("events.whea.error.count")]
    [InlineData("performance.cpu.percent")]
    public async Task AbsentCoverageCannotAuthorizeLoadEvenWithAValue(string key) =>
        await AssertNoProbeStartsAsync(Report(SafeData().Select(datum => datum.MetricKey == key
            ? datum with { Coverage = EvidenceCoverage.Absent } : datum).ToArray()));

    [Theory]
    [InlineData("storage.volume.free.percent", -1)]
    [InlineData("storage.volume.free.percent", 101)]
    [InlineData("memory.available.bytes", -1)]
    [InlineData("memory.available.bytes", 9223372036854775808d)]
    [InlineData("performance.cpu.percent", double.PositiveInfinity)]
    [InlineData("performance.cpu.percent", 101)]
    [InlineData("events.whea.error.count", -1)]
    [InlineData("events.whea.error.count", 0.5)]
    [InlineData("events.whea.error.count", 9223372036854775808d)]
    [InlineData("events.whea.unknown.count", double.NaN)]
    public async Task InvalidRangeOrCountNeverStartsProbe(string key, double value) =>
        await AssertNoProbeStartsAsync(Report(SafeData().Select(datum => datum.MetricKey == key
            ? datum with { NumericValue = value } : datum).ToArray()));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-1)]
    [InlineData(101)]
    [InlineData(39)]
    public async Task BatteryMustHaveAValidSufficientReading(double percent)
    {
        var data = SafeData().Select(datum => datum.MetricKey == "power.source"
            ? datum with { TextValue = "Batería" } : datum).Append(Datum("power.battery.percent", percent)).ToArray();
        await AssertNoProbeStartsAsync(Report(data));
    }

    [Fact]
    public async Task UnknownSecondVolumeCannotBeHiddenByValidFirstVolume() =>
        await AssertNoProbeStartsAsync(Report([.. SafeData(), DiagnosticDatum.Unknown("Second volume", "synthetic",
            "unavailable", "storage.volume.free.percent")]));

    [Fact]
    public async Task ContradictoryPowerReadingsDoNotStartProbe() =>
        await AssertNoProbeStartsAsync(Report([.. SafeData(), Datum("power.source", text: "Batería")]));

    [Theory]
    [InlineData(EvidenceCoverage.Complete)]
    [InlineData(EvidenceCoverage.Partial)]
    public void UsablePartialEvidenceAndMissingOptionalThermalSensorHaveExplicitLimits(EvidenceCoverage coverage)
    {
        var report = Report(SafeData().Select(datum => datum with { Coverage = coverage }).ToArray());
        Assert.True(StabilityPreflightEvaluator.Evaluate(report, HealthRuleEngine.Evaluate(report)).CanRun);
        Assert.DoesNotContain(report.Sections.SelectMany(section => section.Data), datum =>
            datum.MetricKey == "storage.disk.temperature.celsius");
    }

    private static async Task AssertNoProbeStartsAsync(DiagnosticReport report)
    {
        var probe = new RecordingProbe();
        var store = new InMemoryStore();
        var coordinator = new StabilityTestCoordinator([probe], store, ZeroDurations(), new SafeSafetySource(report));
        var result = await coordinator.RunAsync(report, HealthRuleEngine.Evaluate(report));
        Assert.Equal(0, probe.Calls);
        Assert.False(store.Started);
        Assert.False(result.Preflight.CanRun);
    }

    private static int SummaryScore(SessionReport report)
    {
        var match = Regex.Match(ReportRenderer.ToText(report), @"Evaluación orientativa: (\d+)/100\.");
        Assert.True(match.Success, "Expected an explicit score in the synthetic summary.");
        return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    private static StabilityTestResult StabilityFailure(Priority priority) => new(
        DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(1), new([], true),
        [new("memory", "Synthetic memory probe", StabilityProbeStatus.Failed, TimeSpan.Zero,
            "Synthetic failure; no workload executed", new Dictionary<string, double>())], false, false,
        [new("STABILITY-SYNTHETIC-FAILED", HealthRuleEngine.RuleSetVersion, HealthDomain.LoadStability,
            priority, RiskLevel.R0, "Synthetic failure", "Review before any load", "in-memory fixture")]);

    private static DiagnosticReport WithOperationalStatus(string status) => Report(SafeData()
        .Select(datum => datum.MetricKey == "storage.disk.operational"
            ? datum with { TextValue = status, Value = status } : datum).ToArray());

    private static StabilityTestOptions ZeroDurations() => new(TimeSpan.Zero, TimeSpan.Zero,
        TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);
    private static DiagnosticReport SafeReport() => Report(SafeData());
    private static DiagnosticReport Report(DiagnosticDatum[] data) => new(DateTimeOffset.UnixEpoch,
        [new("synthetic", "Synthetic fixture", EvidenceCoverage.Complete, data, [])], true);
    private static DiagnosticDatum[] SafeData() =>
    [
        Datum("power.source", text: "Corriente alterna"),
        Datum("storage.disk.health", text: "Healthy"),
        Datum("storage.disk.operational", text: "OK"),
        Datum("storage.volume.free.percent", number: 50),
        Datum("memory.available.bytes", number: 4L * 1024 * 1024 * 1024),
        Datum("memory.load.percent", number: 30),
        Datum("performance.cpu.percent", number: 10),
        Datum("events.whea.error.count", number: 0),
        Datum("events.whea.unknown.count", number: 0),
    ];

    private static DiagnosticDatum Datum(string key, double? number = null, string? text = null) =>
        DiagnosticDatum.Available(key, text ?? number?.ToString(CultureInfo.InvariantCulture) ?? "fixture",
            "synthetic", metricKey: key, numericValue: number, textValue: text);

    private sealed class RecordingProbe : IStabilityProbe
    {
        public string Id => "cpu";
        public string Label => "Recording fake; no CPU workload";
        public TimeSpan Timeout => TimeSpan.FromSeconds(1);
        public int Calls { get; private set; }
        public Task<StabilityProbeResult> ExecuteAsync(StabilityProbeContext context, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new StabilityProbeResult(Id, Label, StabilityProbeStatus.Passed,
                TimeSpan.Zero, "No hardware accessed", new Dictionary<string, double>()));
        }
    }

    private sealed class InMemoryStore : IStabilitySessionStore
    {
        public bool Started { get; private set; }
        public Task<InterruptedStabilitySession?> ReadInterruptedAsync(CancellationToken cancellationToken) => Task.FromResult<InterruptedStabilitySession?>(null);
        public Task StartAsync(DateTimeOffset startedAt, CancellationToken cancellationToken) { Started = true; return Task.CompletedTask; }
        public Task UpdateAsync(string completedProbeId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CompleteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class SafeSafetySource(DiagnosticReport report) : IStabilitySafetySource
    {
        public Task<DiagnosticReport> RefreshAsync(CancellationToken cancellationToken) => Task.FromResult(report);
        public Task<StabilityRuntimeSample> SampleAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new StabilityRuntimeSample(4L * 1024 * 1024 * 1024, true, null));
    }
}
