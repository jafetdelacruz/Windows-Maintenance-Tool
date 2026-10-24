using WinMaint.Domain;
using WinMaint.Reporting;

namespace WinMaint.Application.Tests;

public sealed class WindowsSupportPolicyTests
{
    [Theory]
    [InlineData("Core")]
    [InlineData("Professional")]
    [InlineData("ProfessionalWorkstation")]
    public void Windows10HomePro22H2WarnsWithoutAssumingEsuOrChangingCompatibility(string edition)
    {
        var data = WindowsSupportPolicy.Evaluate("Windows 10", edition, "22H2", new(2026, 10, 21));
        Assert.Contains(data, datum => datum.MetricKey == "system.support.notice" && datum.Value.Contains("14 de octubre de 2025", StringComparison.Ordinal));
        Assert.Contains(data, datum => datum.MetricKey == "system.support.status" && datum.TextValue == "standard-ended");
        Assert.DoesNotContain(data, datum => datum.MetricKey == "system.compatibility");
        var esu = Assert.Single(data, datum => datum.MetricKey == "system.esu.enrollment");
        Assert.Equal(EvidenceCoverage.Absent, esu.Coverage);
        Assert.Null(esu.TextValue);
        Assert.Equal(DiagnosticDatum.UnknownValue, esu.Value);
    }

    [Theory]
    [InlineData("EnterpriseS", "21H2")]
    [InlineData("IoTEnterpriseS", "21H2")]
    [InlineData("EnterpriseSN", "1809")]
    [InlineData(null, "22H2")]
    [InlineData("UnknownEdition", "22H2")]
    [InlineData("Professional", "21H2")]
    [InlineData("Professional", null)]
    public void SpecificOrUnidentifiedLifecycleIsNotInvented(string? edition, string? version)
    {
        var data = WindowsSupportPolicy.Evaluate("Windows 10", edition, version, new(2026, 10, 21));
        var lifecycle = Assert.Single(data, datum => datum.MetricKey == "system.support.status");
        Assert.Equal(EvidenceCoverage.Absent, lifecycle.Coverage);
        Assert.Contains("ciclo", lifecycle.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(data, datum => datum.MetricKey == "system.esu.enrollment" && datum.Coverage == EvidenceCoverage.Absent);
    }

    [Theory]
    [InlineData("Windows 11")]
    [InlineData("Windows no compatible")]
    public void OtherSystemsDoNotReceiveWindows10NoticeOrClaimCurrentSupport(string generation)
    {
        var data = WindowsSupportPolicy.Evaluate(generation, "Professional", "25H2", new(2026, 10, 21));
        Assert.DoesNotContain(data, datum => datum.MetricKey == "system.support.notice");
        Assert.Equal(EvidenceCoverage.Absent, Assert.Single(data).Coverage);
    }

    [Fact]
    public void HistoricalDateDoesNotClaimFutureEndAlreadyHappened()
    {
        var data = WindowsSupportPolicy.Evaluate("Windows 10", "Professional", "22H2", new(2025, 10, 13));
        Assert.DoesNotContain(data, datum => datum.TextValue == "standard-ended");
    }

    [Fact]
    public void NoticeAndUnknownEsuSurviveJsonAndReadableTextWithoutHealthPenalty()
    {
        var data = WindowsSupportPolicy.Evaluate("Windows 10", "Professional", "22H2", new(2026, 10, 21));
        var section = new DiagnosticSection("system", "Sistema", EvidenceCoverage.Partial, data, []);
        var diagnostic = new DiagnosticReport(DateTimeOffset.UtcNow, [section], true);
        var journal = new ReportJournal("unused-no-io");
        journal.AddSnapshot(diagnostic);
        var report = ReportRenderer.Parse(ReportRenderer.ToJson(journal.CreateReport("Completed")));
        Assert.Null(report.FinalHealth!.OverallScore);
        Assert.Empty(report.FinalHealth.Findings);
        Assert.Contains(report.Snapshots[0].Diagnostic.Sections[0].Data,
            datum => datum.MetricKey == "system.esu.enrollment" && datum.Coverage == EvidenceCoverage.Absent);
        var text = ReportRenderer.ToText(report);
        Assert.Contains("soporte estándar terminado", text);
        Assert.Contains("Inscripción ESU: desconocida", text);
    }
}
