using System.Text.Json;
using System.Text.Json.Nodes;
using WinMaint.Domain;
using WinMaint.Reporting;

namespace WinMaint.Application.Tests;

public sealed class CleanupRecoveryReportTests
{
    [Fact]
    public void InterruptedAttemptRoundTripsVerifiedUncertainAndNotAttemptedSeparately()
    {
        var record = Record();
        var parsed = ReportRenderer.Parse(ReportRenderer.ToJson(Report(record)));
        Assert.Equal(record.JournalAttemptId, parsed.TemporaryCleanup[0].JournalAttemptId);
        Assert.Equal("third", Assert.Single(parsed.TemporaryCleanup[0].NotAttemptedCandidates));
        Assert.Equal(1, parsed.TemporaryCleanup[0].DeletedFiles);
        Assert.Equal(18, parsed.TemporaryCleanup[0].DeletedLogicalBytes);
        var text = ReportRenderer.ToText(parsed);
        Assert.Contains("1 eliminaciones verificadas, 18 bytes", text);
        Assert.Contains("sin verificación", text);
        Assert.Contains("no intentados: 1", text);
        Assert.Contains("No se reanuda automáticamente", text);
    }

    [Theory]
    [InlineData("counter-files")]
    [InlineData("counter-bytes")]
    [InlineData("counter-overflow")]
    [InlineData("physical")]
    [InlineData("duplicate")]
    [InlineData("duplicate-untouched")]
    [InlineData("overlap")]
    [InlineData("missing-untouched")]
    [InlineData("null-untouched")]
    [InlineData("empty-attempt")]
    [InlineData("empty-inventory")]
    [InlineData("negative-count")]
    [InlineData("insufficient-bytes")]
    [InlineData("negative-native-error")]
    [InlineData("completed-with-unknown")]
    [InlineData("skips-with-unknown")]
    [InlineData("inventory-with-results")]
    [InlineData("path-in-id")]
    public void ContradictoryCleanupJsonIsRejectedRatherThanNormalized(string corruption)
    {
        var root = JsonNode.Parse(ReportRenderer.ToJson(Report(Record())))!;
        var record = root["temporaryCleanup"]![0]!;
        var inventory = record["inventory"]!;
        var files = record["files"]!.AsArray();
        switch (corruption)
        {
            case "counter-files": record["deletedFiles"] = 7; break;
            case "counter-bytes": record["deletedLogicalBytes"] = 0; break;
            case "counter-overflow": record["deletedLogicalBytes"] = JsonNode.Parse("999999999999999999999"); break;
            case "physical": record["physicalSpaceFreed"] = "18"; break;
            case "duplicate": files[1]!["candidateId"] = "first"; break;
            case "duplicate-untouched": record["notAttemptedCandidates"]!.AsArray().Add("third"); break;
            case "overlap": record["notAttemptedCandidates"]![0] = "first"; break;
            case "missing-untouched": record["notAttemptedCandidates"] = new JsonArray(); break;
            case "null-untouched": record["notAttemptedCandidates"] = null; break;
            case "empty-attempt": record["journalAttemptId"] = Guid.Empty; break;
            case "empty-inventory": inventory["id"] = Guid.Empty; break;
            case "negative-count": inventory["recentFiles"] = -1; break;
            case "insufficient-bytes": inventory["candidateLogicalBytes"] = 17; break;
            case "negative-native-error": files[1]!["nativeError"] = -1; break;
            case "completed-with-unknown": record["status"] = "Completed"; break;
            case "skips-with-unknown": record["status"] = "FinishedWithSkips"; break;
            case "inventory-with-results": record["status"] = "InventoryOnly"; break;
            case "path-in-id": files[0]!["candidateId"] = @"C:\private\file.txt"; break;
        }
        Assert.Throws<JsonException>(() => ReportRenderer.Parse(root.ToJsonString()));
    }

    private static TemporaryCleanupRecord Record() => new(
        new(Guid.NewGuid(), DateTimeOffset.UtcNow, EvidenceCoverage.Complete, 3, 54, 0, 0, 0),
        "Interrupted", [new("first", TemporaryFileOutcome.DeletedVerified, 18),
            new("second", TemporaryFileOutcome.Unverified, null)], "Recovered, no resumption")
    { JournalAttemptId = Guid.NewGuid(), NotAttemptedCandidates = ["third"] };

    private static SessionReport Report(TemporaryCleanupRecord record) => new("1.2", Guid.NewGuid(),
        "Test", "Fixture", "Windows Fixture", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
        "Partial", [], [], [], [], [])
    { TemporaryCleanup = [record] };
}
