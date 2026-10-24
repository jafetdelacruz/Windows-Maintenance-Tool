using System.Text.Json.Nodes;
using WinMaint.Reporting;
using WinMaint.Domain;

namespace WinMaint.Application.Tests;

public sealed class ReadOnlyReportAcceptanceTests
{
    [Fact]
    public void EmptyRequirementDoesNotAcceptNonemptyInventory()
    {
        Assert.True(ReadOnlyReportAcceptanceVerifier.Verify(Json(), Requirements with { RequireEmptyInventory = true }).Accepted);
        var node = JsonNode.Parse(Json())!;
        node["temporaryCleanup"]![0]!["inventory"]!["candidateFiles"] = 1;
        Assert.False(ReadOnlyReportAcceptanceVerifier.Verify(node.ToJsonString(), Requirements with { RequireEmptyInventory = true }).Accepted);
    }

    [Fact]
    public void RequiredMetricCanSelectCanonicalVolumeWithoutRejectingOtherVolumes()
    {
        var source = ReportRenderer.Parse(Json());
        var diagnostic = source.Snapshots[0].Diagnostic;
        var volumes = new DiagnosticSection("storage", "Fixture volumes", EvidenceCoverage.Complete,
            [DiagnosticDatum.Available(@"Volumen C:\", "Fixture", "Fixture", metricKey: "storage.volume.free.percent", numericValue: 50),
             DiagnosticDatum.Available(@"Volumen D:\", "Fixture", "Fixture", metricKey: "storage.volume.free.percent", numericValue: 25)], []);
        var journal = new ReportJournal("unused-no-io");
        journal.AddSnapshot(diagnostic with { Sections = [.. diagnostic.Sections, volumes] });
        journal.AddPreparedTemporaryInventory(source.TemporaryCleanup[0].Inventory);
        var report = journal.CreateReport("Completed") with { ToolVersion = Requirements.ExactToolVersion };
        var scoped = Requirements with
        {
            RequiredMetrics = [.. Requirements.RequiredMetrics,
            new("storage", "storage.volume.free.percent", @"Volumen C:\")]
        };
        Assert.True(ReadOnlyReportAcceptanceVerifier.Verify(ReportRenderer.ToJson(report), scoped).Accepted);
        Assert.False(ReadOnlyReportAcceptanceVerifier.Verify(ReportRenderer.ToJson(report), scoped with
        { RequiredMetrics = [.. Requirements.RequiredMetrics, new("storage", "storage.volume.free.percent")] }).Accepted);
    }
    private static readonly ReadOnlyReportRequirements Requirements = new("Fixture-candidate", 1, 1, false,
        [new("system", "system.compatibility"), new("performance", "memory.load.percent"),
            new("events", "events.whea.error.count")]);

    [Fact]
    public void EmptyInventoryIsValidButCannotAccreditNonemptyCoverage()
    {
        var json = Json();
        Assert.True(ReadOnlyReportAcceptanceVerifier.Verify(json, Requirements).Accepted);
        var nonempty = ReadOnlyReportAcceptanceVerifier.Verify(json, Requirements with { RequireNonEmptyInventory = true });
        Assert.False(nonempty.Accepted);
        Assert.Contains("nonempty-complete-inventory-not-exercised", nonempty.Failures);
        var root = JsonNode.Parse(json)!;
        root["temporaryCleanup"]![0]!["inventory"]!["candidateFiles"] = 1;
        Assert.True(ReadOnlyReportAcceptanceVerifier.Verify(root.ToJsonString(),
            Requirements with { RequireNonEmptyInventory = true }).Accepted); // A zero-byte candidate is possible.
    }

    [Theory]
    [InlineData("schemaVersion", "1.2")]
    [InlineData("SCHEMAVERSION", "1.2")]
    [InlineData("status", "Completed")]
    public void DuplicatePropertiesAreRejectedBeforeNormalization(string property, string value)
    {
        string json = Json();
        string duplicate = json.Insert(1, System.Text.Json.JsonSerializer.Serialize(property) + ":" +
            System.Text.Json.JsonSerializer.Serialize(value) + ",");
        Assert.False(ReadOnlyReportAcceptanceVerifier.Verify(duplicate, Requirements).Accepted);
    }

    [Theory]
    [InlineData("empty-diagnostic")]
    [InlineData("missing-required-provider")]
    [InlineData("absent-required-provider")]
    [InlineData("missing-required-metric")]
    [InlineData("duplicate-required-metric")]
    [InlineData("unknown-required-metric")]
    [InlineData("invalid-usage")]
    [InlineData("negative-count")]
    [InlineData("fractional-count")]
    [InlineData("wrong-version")]
    [InlineData("empty-session")]
    [InlineData("empty-export")]
    [InlineData("missing-counter")]
    [InlineData("wrong-counter")]
    [InlineData("wrong-inventory-count")]
    [InlineData("duplicate-provider")]
    [InlineData("mutable-mode")]
    [InlineData("invalid-date")]
    [InlineData("missing-name")]
    [InlineData("unknown-inventory")]
    [InlineData("partial-inventory")]
    [InlineData("forged-score")]
    [InlineData("forged-snapshot-score")]
    [InlineData("wrong-rules")]
    [InlineData("wrong-schema")]
    [InlineData("empty-object")]
    public void StructuralOrProcessSuccessCannotReplaceFunctionalEvidence(string corruption)
    {
        var root = JsonNode.Parse(Json())!;
        var sections = root["snapshots"]![0]!["diagnostic"]!["sections"]!.AsArray();
        var memory = sections[1]!["data"]![0]!;
        switch (corruption)
        {
            case "empty-diagnostic": sections.Clear(); break;
            case "missing-required-provider": sections.RemoveAt(2); break;
            case "absent-required-provider": sections[2]!["coverage"] = "Absent"; break;
            case "missing-required-metric": sections[1]!["data"]!.AsArray().Clear(); break;
            case "duplicate-required-metric": sections[1]!["data"]!.AsArray().Add(memory.DeepClone()); break;
            case "unknown-required-metric": memory["coverage"] = "Absent"; break;
            case "invalid-usage": memory["numericValue"] = 101; break;
            case "negative-count": sections[2]!["data"]![0]!["numericValue"] = -1; break;
            case "fractional-count": sections[2]!["data"]![0]!["numericValue"] = 0.5; break;
            case "wrong-version": root["toolVersion"] = "Other"; break;
            case "empty-session": root["sessionId"] = Guid.Empty; break;
            case "empty-export": root["exportId"] = null; break;
            case "missing-counter": root["temporaryCleanup"]![0]!.AsObject().Remove("deletedFiles"); break;
            case "wrong-counter": root["temporaryCleanup"]![0]!["deletedFiles"] = 1; break;
            case "wrong-inventory-count": root["temporaryCleanup"]!.AsArray().Clear(); break;
            case "duplicate-provider": sections.Add(sections[1]!.DeepClone()); break;
            case "mutable-mode": root["snapshots"]![0]!["diagnostic"]!["isReadOnly"] = false; break;
            case "invalid-date": root["updatedAt"] = "0001-01-01T00:00:00Z"; break;
            case "missing-name": memory["name"] = ""; break;
            case "unknown-inventory": root["temporaryCleanup"]![0]!["inventory"]!["coverage"] = "Absent"; break;
            case "partial-inventory": root["temporaryCleanup"]![0]!["inventory"]!["coverage"] = "Partial"; break;
            case "forged-score": root["finalHealth"]!["overallScore"] = 100; break;
            case "forged-snapshot-score": root["snapshots"]![0]!["health"]!["overallScore"] = 100; break;
            case "wrong-rules": root["finalHealth"]!["ruleSetVersion"] = "old"; break;
            case "wrong-schema": root["schemaVersion"] = "1.0"; break;
            case "empty-object": root = new JsonObject(); break;
        }
        var result = ReadOnlyReportAcceptanceVerifier.Verify(root.ToJsonString(), Requirements);
        Assert.False(result.Accepted);
        Assert.NotEmpty(result.Failures);
    }

    private static string Json()
    {
        var now = DateTimeOffset.UtcNow;
        DiagnosticSection Section(string id, DiagnosticDatum datum) => new(id, id, EvidenceCoverage.Partial, [datum], []);
        var journal = new ReportJournal("unused-no-io");
        journal.AddSnapshot(new(now,
        [
            Section("system", DiagnosticDatum.Available("Compatibility", "Supported", "Fixture", metricKey: "system.compatibility", textValue: "supported")),
            Section("performance", DiagnosticDatum.Available("RAM usage", "50%", "Fixture", metricKey: "memory.load.percent", numericValue: 50)),
            Section("events", DiagnosticDatum.Available("WHEA errors", "0", "Fixture", EvidenceCoverage.Partial, metricKey: "events.whea.error.count", numericValue: 0)),
        ], true));
        journal.AddPreparedTemporaryInventory(new(Guid.NewGuid(), now, EvidenceCoverage.Complete, 0, 0, 0, 0, 0));
        return ReportRenderer.ToJson(journal.CreateReport("Completed") with { ToolVersion = "Fixture-candidate", ExportId = Guid.NewGuid() });
    }
}
