using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using WinMaint.Domain;
using WinMaint.Reporting;

namespace WinMaint.Application.Tests;

// Synthetic contract fixture, never executed as a native binary; not evidence of real deletions.
internal sealed class SyntheticCleanupEvidenceFixture : IDisposable
{
    private static readonly string[] PackageNames = ["WinMaint.CleanupLab.exe", "Run-Lab.ps1", "profile.wsb"];
    private sealed record Scenario(string Status, int Candidates, int Untouched, TemporaryFileOutcome[] Outcomes, long Bytes);
    private readonly string root = Path.Combine(Path.GetTempPath(), "WinMaintEvidenceTests", Guid.NewGuid().ToString("N"));
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };
    internal string Package => Path.Combine(root, "package");
    internal string Results { get; }
    internal string ExpectedHash { get; }

    internal SyntheticCleanupEvidenceFixture()
    {
        Results = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Package);
        Directory.CreateDirectory(Results);
        foreach (string name in PackageNames)
            File.WriteAllText(Path.Combine(Package, name), "Synthetic verifier input only: " + name);
        ExpectedHash = Hash(Path.Combine(Package, "WinMaint.CleanupLab.exe"));
        Directory.CreateDirectory(Path.Combine(Package, "source-snapshot"));
        File.WriteAllText(Path.Combine(Package, "source-snapshot", "source.cs"), "Synthetic source identity fixture, not a reproducible application.");
        Write(Path.Combine(Package, "package-manifest.json"), new
        {
            SchemaVersion = "cleanup-package-1.0",
            LabVersion = "0.9.1",
            Sdk = "10.0.400",
            ProductionEnabled = false,
            Artifacts = PackageNames.Select(name => new { Name = name, Sha256 = Hash(Path.Combine(Package, name)) }),
            Sources = new[] { new { Path = "source.cs", Sha256 = Hash(Path.Combine(Package, "source-snapshot", "source.cs")) } },
        });
        var scenarios = Scenarios();
        var now = DateTimeOffset.UtcNow;
        var diagnostic = new DiagnosticReport(now.AddSeconds(-1), [new("fixture", "Synthetic R0", EvidenceCoverage.Partial,
            [DiagnosticDatum.Available("Compatibility", "Supported", "Fixture", metricKey: "system.compatibility", textValue: "supported"),
             DiagnosticDatum.Available(@"Volumen C:\", "Fixture", "Fixture", metricKey: "storage.volume.free.percent", numericValue: 50)], [])], true);
        foreach (var pair in scenarios)
        {
            var s = pair.Value;
            var inventory = new TemporaryInventory(Guid.NewGuid(), now, pair.Key == "guard-actual-partial" ? EvidenceCoverage.Partial : EvidenceCoverage.Complete,
                s.Candidates, pair.Key == "exclusions" ? 36 : s.Candidates * 18,
                pair.Key == "exclusions" ? 3 : 0, pair.Key == "exclusions" ? 7 : 0, pair.Key == "guard-actual-partial" ? 1 : 0);
            var journal = new ReportJournal("unused-no-product-write");
            journal.AddSnapshot(diagnostic);
            journal.AddPreparedTemporaryInventory(inventory);
            Guid session = journal.CreateReport("Partial").SessionId;
            var ids = Enumerable.Range(0, s.Candidates).Select(_ => Guid.NewGuid().ToString("N")).ToArray();
            long remaining = s.Bytes;
            var files = s.Outcomes.Select((outcome, index) =>
            {
                long? bytes = outcome == TemporaryFileOutcome.Unverified ? null : 0;
                if (outcome == TemporaryFileOutcome.DeletedVerified) { bytes = Math.Min(18, remaining); remaining -= bytes.Value; }
                return new TemporaryFileResult(ids[index], outcome, bytes);
            }).ToArray();
            bool attempted = !(pair.Key.StartsWith("cancel-", StringComparison.Ordinal) || pair.Key.StartsWith("guard-", StringComparison.Ordinal));
            var result = new TemporaryCleanupRecord(inventory, s.Status, files, "Synthetic, no native execution")
            {
                JournalAttemptId = attempted ? Guid.NewGuid() : null,
                DiagnosticSessionId = attempted ? session : null,
                DiagnosticCapturedAt = attempted ? diagnostic.CapturedAt : null,
                NotAttemptedCandidates = attempted ? ids.Skip(files.Length).Take(s.Untouched).ToArray() : [],
            };
            if (pair.Key == "guard-partial-plan") result = result with { Inventory = inventory with { Coverage = EvidenceCoverage.Partial } };
            if (pair.Key == "guard-expired-plan") result = result with { Inventory = inventory with { CapturedAt = now.AddMinutes(-6) } };
            journal.AddTemporaryCleanup(result);
            var report = journal.CreateReport("Partial") with
            {
                ToolVersion = "WinMaint v1.0.0-rc.5-dev — revisión local, producción bloqueada",
            };
            File.WriteAllText(Path.Combine(Results, pair.Key + ".json"), ReportRenderer.ToJson(report));
            File.WriteAllText(Path.Combine(Results, pair.Key + ".txt"), ReportRenderer.ToText(report));
            if (pair.Key.StartsWith("crash-", StringComparison.Ordinal))
            {
                var checkpoints = new List<TemporaryCleanupCheckpoint>
                {
                    new(result.JournalAttemptId!.Value, 0, TemporaryCleanupCheckpointKind.Started, inventory, ids,
                        DiagnosticSessionId: session, DiagnosticCapturedAt: diagnostic.CapturedAt),
                    new(result.JournalAttemptId.Value, 1, TemporaryCleanupCheckpointKind.Intent, CandidateId: ids[0]),
                };
                if (pair.Key == "crash-outcome") checkpoints.Add(new(result.JournalAttemptId.Value, 2, TemporaryCleanupCheckpointKind.Outcome, Result: files[0]));
                string previous = string.Empty;
                foreach (var checkpoint in checkpoints)
                {
                    string payload = JsonSerializer.Serialize(checkpoint, Json);
                    string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(previous + "\n" + payload)));
                    Write(Path.Combine(Results, pair.Key + "-checkpoint-" + checkpoint.Sequence.ToString("D8", System.Globalization.CultureInfo.InvariantCulture) + ".json"),
                        new { SchemaVersion = "temporary-cleanup-journal-1.1", Payload = payload, PreviousHash = previous, Hash = hash });
                    previous = hash;
                }
            }
        }
        var checks = new List<string> { "recreation", "cancel-confirmation", "cancel-token", "public-production-block",
            "exclusions/prepared", "exclusions/native-effects", "guard-unknown-id/native-map", "mid-cancellation", "error-after-deletion", "late-diagnostic" };
        foreach (string name in scenarios.Keys)
        {
            checks.Add(name + "/product-report");
            if (name.StartsWith("substitution-", StringComparison.Ordinal))
            { checks.Add(name + "/prepared"); checks.Add(name + "/native-effects"); }
            if (name.StartsWith("guard-", StringComparison.Ordinal)) checks.Add(name);
            if (name.StartsWith("persistence-failure-", StringComparison.Ordinal))
            { checks.Add(name + "/effects"); if (name != "persistence-failure-0") checks.Add(name + "/recovery"); }
            if (name.StartsWith("crash-", StringComparison.Ordinal))
                foreach (string suffix in new[] { "terminated-owned-child", "physical-state", "recovery", "no-resumption", "original-diagnostic-and-inventory" }) checks.Add(name + "/" + suffix);
        }
        Write(Path.Combine(Results, "manifest.json"), new
        {
            SchemaVersion = "definitive-cleanup-lab-1.0",
            LabVersion = "0.9.1",
            BatchId = Path.GetFileName(Results),
            ProductionEnabled = false,
            Passed = true,
            Error = (string?)null,
            ExecutableSha256 = ExpectedHash,
            Checks = checks.Select(id => new { Id = id, Passed = true }),
            Artifacts = Directory.GetFiles(Results).Select(path => new { Name = Path.GetFileName(path), Sha256 = Hash(path) }).ToArray(),
        });
    }

    internal void Change(string filename, Action<JsonNode> mutate, bool updateChecksum = true)
    {
        string path = Path.Combine(Results, filename);
        var node = JsonNode.Parse(File.ReadAllText(path))!;
        mutate(node);
        File.WriteAllText(path, node.ToJsonString());
        if (updateChecksum)
        {
            UpdateHash(filename);
            if (!filename.Contains("checkpoint", StringComparison.Ordinal))
            {
                try
                {
                    var report = ReportRenderer.Parse(node.ToJsonString());
                    string textName = Path.ChangeExtension(filename, ".txt");
                    File.WriteAllText(Path.Combine(Results, textName), ReportRenderer.ToText(report));
                    UpdateHash(textName);
                }
                catch (JsonException) { /* Deliberately invalid product input must remain invalid. */ }
            }
        }
    }
    internal void UpdateHash(string filename)
    {
        string manifestPath = Path.Combine(Results, "manifest.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!;
        var entry = manifest["artifacts"]!.AsArray().Single(item => item!["name"]!.GetValue<string>() == filename)!;
        entry["sha256"] = Hash(Path.Combine(Results, filename));
        File.WriteAllText(manifestPath, manifest.ToJsonString());
    }
    internal static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }
    private static void Write<T>(string path, T value) => File.WriteAllText(path, JsonSerializer.Serialize(value, Json));
    public void Dispose() => Directory.Delete(root, recursive: true);

    private static Dictionary<string, Scenario> Scenarios()
    {
        var s = new Dictionary<string, Scenario>(StringComparer.Ordinal)
        {
            ["cancel-confirmation"] = new("Cancelled", 1, 0, [], 0),
            ["cancel-token"] = new("Cancelled", 1, 0, [], 0),
            ["exclusions"] = new("Completed", 3, 0, [TemporaryFileOutcome.DeletedVerified, TemporaryFileOutcome.DeletedVerified, TemporaryFileOutcome.DeletedVerified], 36),
            ["mid-cancellation"] = new("Cancelled", 2, 1, [TemporaryFileOutcome.DeletedVerified], 18),
            ["error-after-deletion"] = new("Failed", 1, 0, [TemporaryFileOutcome.Unverified], 0),
            ["late-diagnostic"] = new("Interrupted", 2, 1, [TemporaryFileOutcome.BlockedStale], 0),
        };
        foreach (string name in new[] { "busy", "changed", "identity", "symlink", "hardlink", "readonly", "parent", "root", "parent-link", "root-link", "parent-system", "native-stale", "root-pinned" })
        {
            var outcome = name switch
            {
                "busy" => TemporaryFileOutcome.SkippedBusy,
                "native-stale" => TemporaryFileOutcome.BlockedStale,
                "root-pinned" => TemporaryFileOutcome.DeletedVerified,
                "root" or "parent" or "parent-link" or "root-link" or "parent-system" => TemporaryFileOutcome.BlockedScope,
                _ => TemporaryFileOutcome.SkippedChanged
            };
            s.Add("substitution-" + name, new(name == "root-pinned" ? "Completed" : "FinishedWithSkips", 1, 0, [outcome], name == "root-pinned" ? 18 : 0));
        }
        foreach (string name in new[] { "unknown-id", "partial-plan", "expired-plan", "empty-diagnostic", "actual-partial" }) s.Add("guard-" + name, new("Blocked", 1, 0, [], 0));
        for (int sequence = 0; sequence <= 5; sequence++)
        {
            int verified = sequence == 0 ? 0 : (sequence - 1) / 2;
            var outcomes = Enumerable.Repeat(TemporaryFileOutcome.DeletedVerified, verified).ToList();
            if (sequence is 2 or 4) outcomes.Add(TemporaryFileOutcome.Unverified);
            s.Add("persistence-failure-" + sequence.ToString(System.Globalization.CultureInfo.InvariantCulture), new(sequence == 0 ? "Blocked" : "Interrupted", 2, 2 - sequence / 2, outcomes.ToArray(), verified * 18));
        }
        foreach (string name in new[] { "intent", "after-deletion", "outcome" }) s.Add("crash-" + name, new("Interrupted", 2, 1,
            [name == "outcome" ? TemporaryFileOutcome.DeletedVerified : TemporaryFileOutcome.Unverified], name == "outcome" ? 18 : 0));
        return s;
    }
}
