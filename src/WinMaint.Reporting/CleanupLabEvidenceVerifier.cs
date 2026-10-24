using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WinMaint.Application;
using WinMaint.Domain;

namespace WinMaint.Reporting;

public sealed record CleanupLabEvidenceAcceptance(bool Accepted, int ValidatedChecks, int ValidatedReports,
    IReadOnlyList<string> Failures);

// Read-only acceptance of versioned lab profiles. Never launches a process or repairs evidence.
public static class CleanupLabEvidenceVerifier
{
    // Frozen identity for accepted lab profiles 0.9.0/0.9.1, not today's CLI identity.
    private const string HistoricalLabToolVersion = "WinMaint v1.0.0-rc.5-dev — revisión local, producción bloqueada";
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };
    private sealed record NamedHash(string Name, string Sha256);
    private sealed record SourceHash(string Path, string Sha256);
    private sealed record Package(string SchemaVersion, string LabVersion, string Sdk, bool ProductionEnabled,
        IReadOnlyList<NamedHash> Artifacts, IReadOnlyList<SourceHash> Sources);
    private sealed record Check(string Id, bool Passed);
    private sealed record Manifest(string SchemaVersion, string LabVersion, string BatchId, bool ProductionEnabled,
        bool Passed, string? Error, string ExecutableSha256, IReadOnlyList<Check> Checks, IReadOnlyList<NamedHash> Artifacts);
    private sealed record Envelope(string SchemaVersion, string Payload, string PreviousHash, string Hash);
    private sealed record Scenario(string Status, int Candidates, int NotAttempted, TemporaryFileOutcome[] Outcomes,
        long DeletedBytes);

    public static CleanupLabEvidenceAcceptance Verify(string packageDirectory, string resultDirectory, string expectedExecutableSha256)
    {
        var failures = new List<string>();
        int reports = 0, checks = 0;
        try
        {
            if (!ValidHash(expectedExecutableSha256)) return new(false, 0, 0, ["invalid-expected-executable-hash"]);
            var package = Read<Package>(Path.Combine(packageDirectory, "package-manifest.json"));
            if (package.SchemaVersion != "cleanup-package-1.0" || package.LabVersion is not ("0.9.0" or "0.9.1") ||
                package.Sdk != "10.0.400" || package.ProductionEnabled || package.Sources is not { Count: > 0 })
                return new(false, 0, 0, ["invalid-package-profile"]);
            var packageArtifacts = ValidateFiles(packageDirectory, package.Artifacts,
                ["WinMaint.CleanupLab.exe", "Run-Lab.ps1", "profile.wsb"]);
            if (!packageArtifacts || !Hash(Path.Combine(packageDirectory, "WinMaint.CleanupLab.exe")).Equals(expectedExecutableSha256, StringComparison.OrdinalIgnoreCase))
                return new(false, 0, 0, ["package-or-expected-hash-mismatch"]);
            if (package.Sources.Select(source => source.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != package.Sources.Count ||
                package.Sources.Any(source => !ValidHash(source.Sha256) ||
                    Hash(Contained(Path.Combine(packageDirectory, "source-snapshot"), source.Path)) != source.Sha256))
                return new(false, 0, 0, ["source-snapshot-mismatch"]);

            var manifest = Read<Manifest>(Path.Combine(resultDirectory, "manifest.json"));
            if (manifest.SchemaVersion != "definitive-cleanup-lab-1.0" || manifest.LabVersion != package.LabVersion ||
                manifest.ProductionEnabled || !manifest.Passed || manifest.Error is not null ||
                !Guid.TryParseExact(manifest.BatchId, "N", out _) ||
                Path.GetFileName(Path.GetFullPath(resultDirectory).TrimEnd(Path.DirectorySeparatorChar)) != manifest.BatchId ||
                !manifest.ExecutableSha256.Equals(expectedExecutableSha256, StringComparison.OrdinalIgnoreCase))
                return new(false, 0, 0, ["invalid-or-failed-native-manifest"]);
            var scenarios = Scenarios(package.LabVersion);
            var expectedChecks = ExpectedChecks(scenarios.Keys);
            if (manifest.Checks is null || manifest.Checks.Any(check => check is null || !check.Passed) ||
                !manifest.Checks.Select(check => check.Id).Order(StringComparer.Ordinal).SequenceEqual(expectedChecks.Order(StringComparer.Ordinal)))
                failures.Add("wrong-native-check-set-or-failed-check");
            else checks = manifest.Checks.Count;
            var names = scenarios.Keys.SelectMany(name => new[] { name + ".json", name + ".txt" }).ToList();
            foreach (string boundary in new[] { "intent", "after-deletion", "outcome" })
                for (int index = 0; index < (boundary == "outcome" ? 3 : 2); index++)
                    names.Add("crash-" + boundary + "-checkpoint-" + index.ToString("D8", CultureInfo.InvariantCulture) + ".json");
            if (!ValidateFiles(resultDirectory, manifest.Artifacts, names) ||
                Directory.GetFiles(resultDirectory).Length != names.Count + 1)
                return new(false, checks, 0, [.. failures, "wrong-artifact-set-or-hashes"]);
            var sessionIds = new HashSet<Guid>();
            foreach (var pair in scenarios)
            {
                string name = pair.Key;
                var scenario = pair.Value;
                string json = ReadText(Contained(resultDirectory, name + ".json"));
                using (var document = JsonDocument.Parse(json)) NoDuplicates(document.RootElement);
                var report = ReportRenderer.Parse(json);
                var text = ReadText(Contained(resultDirectory, name + ".txt"));
                bool valid = report.SchemaVersion == "1.2" && report.ToolVersion == HistoricalLabToolVersion &&
                    report.SessionId != Guid.Empty && sessionIds.Add(report.SessionId) && report.Status == "Partial" &&
                    report.ExportId is not null && report.ExportId != Guid.Empty && report.StartedAt != default &&
                    report.UpdatedAt >= report.StartedAt && report.Snapshots.Count == 1 &&
                    report.Snapshots[0].Diagnostic.IsReadOnly && report.Actions.Count == 0 && report.Decisions.Count == 0 &&
                    report.StabilityTests.Count == 0 && report.TemporaryCleanup.Count == 2 &&
                    ReportRenderer.IsMatchingExport(json, text) && ReportRenderer.ToText(report) == text;
                if (!valid) { failures.Add("invalid-product-report:" + name); continue; }
                var inventory = report.TemporaryCleanup[0];
                var result = report.TemporaryCleanup[1];
                var diagnosticPlan = new TemporaryCleanupPlan(inventory.Inventory, true, [])
                {
                    SessionId = report.SessionId,
                    DiagnosticEvidence = new(report.SessionId, inventory.Inventory.Id, report.Snapshots[0].Diagnostic),
                };
                if (!TemporaryCleanupDiagnosticPolicy.IsSufficient(diagnosticPlan, inventory.Inventory.CapturedAt))
                    failures.Add("missing-functional-diagnostic-evidence:" + name);
                var expectedHealth = HealthRuleEngine.Evaluate(report.Snapshots[0].Diagnostic);
                if (JsonSerializer.Serialize(report.Snapshots[0].Health) != JsonSerializer.Serialize(expectedHealth) ||
                    JsonSerializer.Serialize(report.FinalHealth) != JsonSerializer.Serialize(HealthRuleEngine.EvaluateSession([expectedHealth], [])))
                    failures.Add("inconsistent-health:" + name);
                if (inventory.Status != "InventoryOnly" || inventory.Files.Count != 0 || inventory.Inventory.Id != result.Inventory.Id ||
                    inventory.Inventory.CandidateFiles != scenario.Candidates || result.Inventory.CandidateFiles != scenario.Candidates ||
                    inventory.Inventory.CandidateLogicalBytes != (name == "exclusions" ? 36 : scenario.Candidates * 18) ||
                    (name == "guard-actual-partial" ? inventory.Inventory.Coverage != EvidenceCoverage.Partial : inventory.Inventory.Coverage != EvidenceCoverage.Complete) ||
                    name == "exclusions" && (inventory.Inventory.RecentFiles != 3 || inventory.Inventory.ProtectedItems != 7) ||
                    result.Status != scenario.Status || !result.Files.Select(file => file.Outcome).SequenceEqual(scenario.Outcomes) ||
                    result.NotAttemptedCandidates.Count != scenario.NotAttempted || result.DeletedLogicalBytes != scenario.DeletedBytes ||
                    result.Files.Any(file => !Guid.TryParseExact(file.CandidateId, "N", out _)) ||
                    result.NotAttemptedCandidates.Any(id => !Guid.TryParseExact(id, "N", out _)) ||
                    result.JournalAttemptId.HasValue && (result.DiagnosticSessionId != report.SessionId ||
                        result.DiagnosticCapturedAt != report.Snapshots[0].Diagnostic.CapturedAt))
                    failures.Add("inconsistent-scenario-results:" + name);
                if (name.StartsWith("crash-", StringComparison.Ordinal))
                    VerifyCrash(resultDirectory, name[6..], report, failures);
                reports++;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or
            InvalidOperationException or ArgumentException or OverflowException or NullReferenceException)
        { failures.Add("unreadable-or-invalid-evidence:" + exception.GetType().Name); }
        return new(failures.Count == 0 && checks is 98 or 100 && reports is 32 or 33, checks, reports, failures);
    }

    private static Dictionary<string, Scenario> Scenarios(string version)
    {
        var result = new Dictionary<string, Scenario>(StringComparer.Ordinal)
        {
            ["cancel-confirmation"] = new("Cancelled", 1, 0, [], 0),
            ["cancel-token"] = new("Cancelled", 1, 0, [], 0),
            ["exclusions"] = new("Completed", 3, 0, Enumerable.Repeat(TemporaryFileOutcome.DeletedVerified, 3).ToArray(), 36),
            ["mid-cancellation"] = new("Cancelled", 2, 1, [TemporaryFileOutcome.DeletedVerified], 18),
            ["error-after-deletion"] = new("Failed", 1, 0, [TemporaryFileOutcome.Unverified], 0),
        };
        foreach (string name in new[] { "busy", "changed", "identity", "symlink", "hardlink", "readonly", "parent", "root", "parent-link", "root-link", "parent-system", "native-stale", "root-pinned" })
        {
            var outcome = name switch
            {
                "busy" => TemporaryFileOutcome.SkippedBusy,
                "root" or "parent" or "root-link" or "parent-link" or "parent-system" => TemporaryFileOutcome.BlockedScope,
                "native-stale" => TemporaryFileOutcome.BlockedStale,
                "root-pinned" => TemporaryFileOutcome.DeletedVerified,
                _ => TemporaryFileOutcome.SkippedChanged,
            };
            result.Add("substitution-" + name, new(name == "root-pinned" ? "Completed" : "FinishedWithSkips",
                1, 0, [outcome], name == "root-pinned" ? 18 : 0));
        }
        foreach (string name in new[] { "unknown-id", "partial-plan", "expired-plan", "empty-diagnostic", "actual-partial" })
            result.Add("guard-" + name, new("Blocked", 1, 0, [], 0));
        for (int sequence = 0; sequence <= 5; sequence++)
        {
            int verified = sequence == 0 ? 0 : (sequence - 1) / 2;
            var outcomes = Enumerable.Repeat(TemporaryFileOutcome.DeletedVerified, verified).ToList();
            if (sequence is 2 or 4) outcomes.Add(TemporaryFileOutcome.Unverified);
            result.Add("persistence-failure-" + sequence.ToString(CultureInfo.InvariantCulture),
                new(sequence == 0 ? "Blocked" : "Interrupted", 2, 2 - sequence / 2, outcomes.ToArray(), verified * 18));
        }
        foreach (string boundary in new[] { "intent", "after-deletion", "outcome" })
            result.Add("crash-" + boundary, new("Interrupted", 2, 1,
                [boundary == "outcome" ? TemporaryFileOutcome.DeletedVerified : TemporaryFileOutcome.Unverified], boundary == "outcome" ? 18 : 0));
        if (version == "0.9.1") result.Add("late-diagnostic", new("Interrupted", 2, 1, [TemporaryFileOutcome.BlockedStale], 0));
        return result;
    }

    private static List<string> ExpectedChecks(IEnumerable<string> scenarioNames)
    {
        var checks = new List<string> { "recreation", "cancel-confirmation", "cancel-token", "public-production-block",
            "exclusions/prepared", "exclusions/native-effects", "guard-unknown-id/native-map", "mid-cancellation", "error-after-deletion" };
        foreach (string name in scenarioNames)
        {
            if (name == "late-diagnostic") checks.Add(name);
            checks.Add(name + "/product-report");
            if (name.StartsWith("substitution-", StringComparison.Ordinal))
            { checks.Add(name + "/prepared"); checks.Add(name + "/native-effects"); }
            if (name.StartsWith("guard-", StringComparison.Ordinal)) checks.Add(name);
            if (name.StartsWith("persistence-failure-", StringComparison.Ordinal))
            { checks.Add(name + "/effects"); if (name != "persistence-failure-0") checks.Add(name + "/recovery"); }
            if (name.StartsWith("crash-", StringComparison.Ordinal))
                foreach (string suffix in new[] { "terminated-owned-child", "physical-state", "recovery", "no-resumption", "original-diagnostic-and-inventory" })
                    checks.Add(name + "/" + suffix);
        }
        return checks;
    }

    private static void VerifyCrash(string directory, string boundary, SessionReport report, List<string> failures)
    {
        var checkpoints = new List<TemporaryCleanupCheckpoint>();
        string previousHash = string.Empty;
        for (int index = 0; index < (boundary == "outcome" ? 3 : 2); index++)
        {
            var envelope = Read<Envelope>(Contained(directory, "crash-" + boundary + "-checkpoint-" + index.ToString("D8", CultureInfo.InvariantCulture) + ".json"));
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(previousHash + "\n" + envelope.Payload)));
            if (envelope.SchemaVersion != "temporary-cleanup-journal-1.1" || envelope.PreviousHash != previousHash || envelope.Hash != hash)
            { failures.Add("invalid-checkpoint-chain:" + boundary); return; }
            using var payload = JsonDocument.Parse(envelope.Payload);
            NoDuplicates(payload.RootElement);
            checkpoints.Add(JsonSerializer.Deserialize<TemporaryCleanupCheckpoint>(envelope.Payload, Options) ?? throw new JsonException());
            previousHash = hash;
        }
        var recorded = report.TemporaryCleanup[^1];
        var recovery = TemporaryCleanupRecoveryEngine.Replay(recorded.JournalAttemptId ?? Guid.Empty, checkpoints);
        if (recovery.IsClosed || recovery.IsCorrupt || recovery.Inventory != recorded.Inventory ||
            recovery.DiagnosticSessionId != report.SessionId || recovery.DiagnosticCapturedAt != report.Snapshots[0].Diagnostic.CapturedAt ||
            !recovery.Files.SequenceEqual(recorded.Files) || !recovery.NotAttempted.SequenceEqual(recorded.NotAttemptedCandidates))
            failures.Add("checkpoint-and-product-report-disagree:" + boundary);
    }

    private static bool ValidateFiles(string directory, IReadOnlyList<NamedHash> entries, IReadOnlyList<string> expected) =>
        entries is not null && entries.All(entry => entry is not null && ValidHash(entry.Sha256)) &&
        entries.Select(entry => entry.Name).Order(StringComparer.Ordinal).SequenceEqual(expected.Order(StringComparer.Ordinal)) &&
        entries.All(entry => Hash(Contained(directory, entry.Name)) == entry.Sha256);
    private static string Contained(string directory, string relative)
    {
        string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(root, relative));
        if (Path.IsPathRooted(relative) || !path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new JsonException("Evidence path is outside the declared folder.");
        if (relative.Contains(':', StringComparison.Ordinal)) throw new JsonException("Alternate data streams are not evidence files.");
        string? current = path;
        while (current is not null && current.Length >= root.TrimEnd(Path.DirectorySeparatorChar).Length)
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new JsonException("Evidence cannot traverse a reparse point.");
            current = Path.GetDirectoryName(current);
        }
        return path;
    }
    private static bool ValidHash(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > 256 * 1024 * 1024) throw new JsonException("Artifact exceeds the hash bound.");
        return Convert.ToHexString(SHA256.HashData(stream));
    }
    private static string ReadText(string path) => new FileInfo(path).Length <= 16 * 1024 * 1024
        ? File.ReadAllText(path) : throw new JsonException("Evidence file exceeds the bound.");
    private static T Read<T>(string path)
    {
        string text = ReadText(path);
        using var document = JsonDocument.Parse(text);
        NoDuplicates(document.RootElement);
        return JsonSerializer.Deserialize<T>(text, Options) ?? throw new JsonException("Empty evidence.");
    }
    private static void NoDuplicates(JsonElement element) => EvidenceJsonValidation.NoDuplicates(element);
}
