using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using WinMaint.Reporting;

namespace WinMaint.Application.Tests;

public sealed class CleanupLabEvidenceVerifierTests
{
    [Fact]
    public void SyntheticContractFixtureIsAcceptedWithoutClaimingNativeExecution()
    {
        using var fixture = new SyntheticCleanupEvidenceFixture();
        var result = CleanupLabEvidenceVerifier.Verify(fixture.Package, fixture.Results, fixture.ExpectedHash);
        Assert.True(result.Accepted, string.Join(",", result.Failures));
        Assert.Equal(100, result.ValidatedChecks);
        Assert.Equal(33, result.ValidatedReports);
    }

    [Theory]
    [InlineData("wrong-expected-hash")]
    [InlineData("source-changed")]
    [InlineData("profile-changed")]
    [InlineData("failed-check")]
    [InlineData("missing-check")]
    [InlineData("duplicate-check")]
    [InlineData("wrong-batch-exe")]
    [InlineData("missing-report")]
    [InlineData("report-checksum")]
    [InlineData("empty-diagnostic")]
    [InlineData("wrong-tool")]
    [InlineData("current-cli-is-not-historical-lab")]
    [InlineData("invalid-outcome")]
    [InlineData("wrong-counter")]
    [InlineData("wrong-status")]
    [InlineData("wrong-diagnostic-session")]
    [InlineData("empty-export")]
    [InlineData("missing-untouched")]
    [InlineData("checkpoint-sequence")]
    [InlineData("duplicate-json-property")]
    public void CorruptedEvidenceIsRejectedEvenWithMatchingFileHashes(string corruption)
    {
        using var fixture = new SyntheticCleanupEvidenceFixture();
        string expected = fixture.ExpectedHash;
        string manifestPath = Path.Combine(fixture.Results, "manifest.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!;
        switch (corruption)
        {
            case "wrong-expected-hash": expected = new string('0', 64); break;
            case "source-changed": File.AppendAllText(Path.Combine(fixture.Package, "source-snapshot", "source.cs"), "changed"); break;
            case "profile-changed": File.AppendAllText(Path.Combine(fixture.Package, "profile.wsb"), "changed"); break;
            case "failed-check": manifest["checks"]![0]!["passed"] = false; break;
            case "missing-check": manifest["checks"]!.AsArray().RemoveAt(0); break;
            case "duplicate-check": manifest["checks"]![0] = manifest["checks"]![1]!.DeepClone(); break;
            case "wrong-batch-exe": manifest["executableSha256"] = new string('0', 64); break;
            case "missing-report": File.Delete(Path.Combine(fixture.Results, "exclusions.json")); break;
            case "report-checksum": File.AppendAllText(Path.Combine(fixture.Results, "exclusions.json"), " "); break;
            case "empty-diagnostic": fixture.Change("exclusions.json", node => node["snapshots"]![0]!["diagnostic"]!["sections"]!.AsArray().Clear()); break;
            case "wrong-tool": fixture.Change("exclusions.json", node => node["toolVersion"] = "Other tool"); break;
            case "current-cli-is-not-historical-lab": fixture.Change("exclusions.json", node => node["toolVersion"] = WinMaint.Domain.ProductIdentity.DisplayVersion); break;
            case "invalid-outcome": fixture.Change("exclusions.json", node => node["temporaryCleanup"]![1]!["files"]![0]!["outcome"] = 999); break;
            case "wrong-counter": fixture.Change("exclusions.json", node => node["temporaryCleanup"]![1]!["deletedLogicalBytes"] = 0); break;
            case "wrong-status": fixture.Change("substitution-busy.json", node => node["temporaryCleanup"]![1]!["status"] = "Cancelled"); break;
            case "wrong-diagnostic-session": fixture.Change("crash-intent.json", node => node["temporaryCleanup"]![1]!["diagnosticSessionId"] = Guid.NewGuid()); break;
            case "empty-export": fixture.Change("exclusions.json", node => node["exportId"] = null); break;
            case "missing-untouched": fixture.Change("crash-intent.json", node => node["temporaryCleanup"]![1]!["notAttemptedCandidates"]!.AsArray().Clear()); break;
            case "checkpoint-sequence":
                fixture.Change("crash-intent-checkpoint-00000001.json", envelope =>
                {
                    var payload = JsonNode.Parse(envelope["payload"]!.GetValue<string>())!;
                    payload["sequence"] = 7;
                    string raw = payload.ToJsonString();
                    envelope["payload"] = raw;
                    envelope["hash"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(envelope["previousHash"]!.GetValue<string>() + "\n" + raw)));
                });
                break;
            case "duplicate-json-property":
                string report = Path.Combine(fixture.Results, "exclusions.json");
                File.WriteAllText(report, File.ReadAllText(report).Insert(1, "\"SCHEMAVERSION\":\"1.2\","));
                fixture.UpdateHash("exclusions.json");
                break;
        }
        // Preserve changes to the manifest made by Change/UpdateHash, unless this case edits the manifest itself.
        if (corruption is "failed-check" or "missing-check" or "duplicate-check" or "wrong-batch-exe")
            File.WriteAllText(manifestPath, manifest.ToJsonString());
        var result = CleanupLabEvidenceVerifier.Verify(fixture.Package, fixture.Results, expected);
        Assert.False(result.Accepted);
        Assert.NotEmpty(result.Failures);
    }
}
