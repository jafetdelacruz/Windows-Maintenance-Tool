using System.Text.Json;
using WinMaint.Application;
using WinMaint.Domain;
using WinMaint.Reporting;

namespace WinMaint.Application.Tests;

public sealed class ReportingTests
{
    [Fact]
    public void ComparisonsRequireSameMetricSourceUnitAndCompleteEvidence()
    {
        var before = Snapshot(DiagnosticDatum.Available("Libre C:", "10%", "Storage", metricKey: "disk.free.percent", numericValue: 10));
        var after = Snapshot(DiagnosticDatum.Available("Libre C:", "20%", "Storage", metricKey: "disk.free.percent", numericValue: 20));
        Assert.Equal(10, Assert.Single(ReportComparison.Compare([before, after])).Delta);
        Assert.Empty(ReportComparison.Compare([before]));
        Assert.Empty(ReportComparison.Compare([before, Snapshot(DiagnosticDatum.Unknown("Libre C:", "Storage", "Sin sensor", "disk.free.percent"))]));
        Assert.Empty(ReportComparison.Compare([before, Snapshot(DiagnosticDatum.Available("Libre C:", "20", "Other", metricKey: "disk.free.percent", numericValue: 20))]));
        Assert.Empty(ReportComparison.Compare([before, Snapshot(DiagnosticDatum.Available("Libre C:", "20", "Storage", metricKey: "disk.free.bytes", numericValue: 20))]));
        Assert.Empty(ReportComparison.Compare([before, Snapshot(DiagnosticDatum.Available("Libre D:", "20", "Storage", metricKey: "disk.free.percent", numericValue: 20))]));
    }

    [Fact]
    public void SerialIsRedactedFromTextJsonAndRepeatedNotes()
    {
        var report = Report([Snapshot(DiagnosticDatum.Available("Número de serie", "SECRET123", "Fixture", detail: "SECRET123"))]) with
        {
            Notes = ["Referencia SECRET123"],
        };
        var json = ReportRenderer.ToJson(report);
        var text = ReportRenderer.ToText(report);
        Assert.DoesNotContain("SECRET123", json);
        Assert.DoesNotContain("SECRET123", text);
        Assert.Contains("SERIAL OCULTO", text);
        using var parsed = JsonDocument.Parse(json);
        Assert.Equal("1.0", parsed.RootElement.GetProperty("schemaVersion").GetString());
        Assert.False(parsed.RootElement.TryGetProperty("client", out _));
        Assert.False(parsed.RootElement.TryGetProperty("folio", out _));
    }

    [Fact]
    public void EmptyPartialReportDoesNotInventHealthOrResults()
    {
        var text = ReportRenderer.ToText(Report([]));
        Assert.Contains("Reporte parcial", text);
        Assert.Contains("no ejecutada", text);
        Assert.Contains("No hay dos capturas", text);
        Assert.Empty(ReportRenderer.Parse(ReportRenderer.ToJson(Report([]))).Snapshots);
    }

    [Fact]
    public async Task AtomicSavePreservesPreviousReportOnCancellationAndLeavesNoTemporaryFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "WinMaintReportingTests", Guid.NewGuid().ToString("N"));
        try
        {
            var journal = new ReportJournal(directory);
            journal.AddSnapshot(Snapshot(DiagnosticDatum.Unknown("Temperatura", "Fixture", "Sin sensor")).Diagnostic);
            await journal.SaveAsync("Partial");
            var original = await File.ReadAllTextAsync(journal.JsonPath);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => journal.SaveAsync("Completed", cancellation.Token));
            Assert.Equal(original, await File.ReadAllTextAsync(journal.JsonPath));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
            Assert.Contains("Información disponible", await File.ReadAllTextAsync(journal.TextPath));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void HumanSummaryDeduplicatesAlertsAndKeepsTechnicalEvidenceInJson()
    {
        var snapshot = Snapshot(DiagnosticDatum.Available("Dato de prueba", "Detalle técnico extenso", "Fuente técnica"));
        var finding = new DiagnosticFinding("WHEA-RECENT", "1.0", HealthDomain.HardwareReliability,
            Priority.P2, RiskLevel.R0, "WHEA", "Revisar", "events.whea.count=4");
        snapshot = snapshot with { Health = snapshot.Health with { Findings = [finding] } };
        var report = Report([snapshot, snapshot]);
        var text = ReportRenderer.ToText(report);
        Assert.Equal(1, text.Split("Windows registró avisos", StringSplitOptions.None).Length - 1);
        Assert.Contains("no confirman una pieza dañada", text);
        Assert.DoesNotContain("Detalle técnico extenso", text);
        Assert.DoesNotContain("events.whea.count=4", text);
        Assert.Contains("events.whea.count=4", ReportRenderer.ToJson(report));
        Assert.True(text.Split('\n').Length < 40);
    }

    private static ReportSnapshot Snapshot(DiagnosticDatum datum)
    {
        var diagnostic = new DiagnosticReport(DateTimeOffset.UnixEpoch,
            [new DiagnosticSection("fixture", "Datos", EvidenceCoverage.Complete, [datum], [])], true);
        return new ReportSnapshot(diagnostic, HealthRuleEngine.Evaluate(diagnostic));
    }

    private static SessionReport Report(IReadOnlyList<ReportSnapshot> snapshots) => new(
        "1.0", Guid.Empty, "Test", "Fixture", "Windows Fixture", DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch, "Partial", snapshots, [], [], [], []);
}
