using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WinMaint.Application;
using WinMaint.Domain;
using WinMaint.Infrastructure.Win;
using WinMaint.Reporting;

namespace WinMaint.CleanupLab.Next;

internal static partial class Program
{
    private const string Mount = @"C:\WinMaintPhase8Cleanup\";
    private const string Output = @"C:\WinMaintLabOutput";
    private const string Marker = @"C:\WinMaint-Cleanup-09.marker";
    private static readonly string Batch = Guid.NewGuid().ToString("N");
    private static readonly List<Check> Checks = [];
    private static readonly List<Artifact> Artifacts = [];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    private static string Destination => Path.Combine(Output, Batch);
    private sealed record Check(string Id, bool Passed);
    private sealed record Artifact(string Name, string Sha256);

    private static async Task<int> Main(string[] args)
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess ||
            !Environment.UserName.Equals("WDAGUtilityAccount", StringComparison.OrdinalIgnoreCase) ||
            !AppContext.BaseDirectory.Equals(Mount, StringComparison.OrdinalIgnoreCase))
        { Console.WriteLine("BLOCKED: owned Windows Sandbox fixture only."); return 3; }
        if (args.Length == 3 && args[0] == "--crash-child" && Guid.TryParseExact(args[1], "N", out var fixture))
            return await RunCrashChildAsync(fixture, args[2]).ConfigureAwait(false);
        if (args.Length != 0) return 3;
        if (File.Exists(Marker)) { Console.WriteLine("RECREATION REQUIRED: close this Sandbox completely."); return 4; }
        if (!Directory.Exists(Output)) return 3;
        Directory.CreateDirectory(Destination);
        await File.WriteAllTextAsync(Marker, Batch).ConfigureAwait(false);
        string? error = null;
        try
        {
            Require("recreation", true);
            var diagnostic = await WindowsDiagnosticComposition.Create().CollectAsync().ConfigureAwait(false);
            await RunCasesAsync(diagnostic).ConfigureAwait(false);
            foreach (string boundary in new[] { "intent", "after-deletion", "outcome" })
                await RunCrashParentAsync(boundary).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            error = exception.GetType().Name + ": " + exception.Message;
            Console.WriteLine("LAB FAILED: " + error);
        }
        var manifest = new
        {
            SchemaVersion = "definitive-cleanup-lab-1.0",
            LabVersion = "0.9.1",
            BatchId = Batch,
            ProductionEnabled = false,
            Passed = error is null,
            Error = error,
            ExecutableSha256 = Hash(Environment.ProcessPath!),
            Checks,
            Artifacts,
        };
        await File.WriteAllTextAsync(Path.Combine(Destination, "manifest.json"), JsonSerializer.Serialize(manifest, Json), new UTF8Encoding(false)).ConfigureAwait(false);
        Console.WriteLine(error is null ? "DEFINITIVE LAB PASSED. PRODUCTION CLEANUP BLOCKED." : "PRODUCTION CLEANUP BLOCKED.");
        return error is null ? 0 : 1;
    }

    private static void Require(string id, bool condition)
    {
        Checks.Add(new(id, condition));
        Console.WriteLine((condition ? "PASS: " : "FAIL: ") + id);
        if (!condition) throw new InvalidOperationException("Acceptance condition failed: " + id);
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static bool Missing(string path)
    {
        try { _ = File.GetAttributes(path); return false; }
        catch (FileNotFoundException) { return true; }
    }

    private sealed class Fixture
    {
        public Guid Id { get; }
        public string Owned { get; }
        public string Profile => Path.Combine(Owned, "synthetic-profile");
        public string Root => Path.Combine(Profile, "AppData", "Local", "Temp");
        public string JournalPath => Path.Combine(Owned, "journal");
        public ReportJournal Report { get; }
        public Fixture(DiagnosticReport diagnostic, Guid? id = null)
        {
            Id = id ?? Guid.NewGuid();
            Owned = OwnedDirectory(Id);
            if (Directory.Exists(Owned)) throw new IOException("Fixture identity cannot be reused.");
            Directory.CreateDirectory(Root);
            Report = new(Path.Combine(Owned, "reports"));
            Report.AddSnapshot(diagnostic);
        }
        public string Make(string relative, bool old = true, string content = "SYNTHETIC FILE 018")
        {
            string path = Path.Combine(Root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content, new UTF8Encoding(false));
            if (old)
            {
                File.SetCreationTimeUtc(path, DateTime.UtcNow.AddDays(-14));
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-14));
            }
            return path;
        }
        public async Task<WindowsTemporaryCleanupPreparation> PrepareAsync(OwnedCleanupLabOptions? options = null)
        {
            var prepared = await WindowsTemporaryCleanupPreparation.CreateForOwnedLabAsync(Profile, CancellationToken.None, options).ConfigureAwait(false);
            Report.AddPreparedTemporaryInventory(prepared.Inventory);
            await Report.SaveAsync("InProgress").ConfigureAwait(false); // A failed pre-execution export aborts the scenario.
            return prepared;
        }
        public TemporaryCleanupPlan Plan(IPreparedTemporaryCleanupRunner runner) =>
            TemporaryCleanupPlan.FromPrepared(runner, Report.CreateReport("InProgress").SessionId,
                Report.CreateReport("InProgress").Snapshots[0].Diagnostic);
        public async Task ExportAsync(string name, TemporaryCleanupRecord record)
        {
            Report.AddTemporaryCleanup(record);
            await Report.SaveAsync("Partial").ConfigureAwait(false);
            var json = await File.ReadAllTextAsync(Report.JsonPath).ConfigureAwait(false);
            var text = await File.ReadAllTextAsync(Report.TextPath).ConfigureAwait(false);
            var parsed = ReportRenderer.Parse(json);
            var saved = parsed.TemporaryCleanup[^1];
            Require(name + "/product-report", ReportRenderer.IsMatchingExport(json, text) &&
                saved.Status == record.Status && saved.DeletedFiles == record.DeletedFiles &&
                saved.DeletedLogicalBytes == record.DeletedLogicalBytes &&
                saved.DiagnosticSessionId == record.DiagnosticSessionId && saved.DiagnosticCapturedAt == record.DiagnosticCapturedAt &&
                saved.Files.SequenceEqual(record.Files) && saved.NotAttemptedCandidates.SequenceEqual(record.NotAttemptedCandidates));
            foreach (string source in new[] { Report.JsonPath, Report.TextPath })
                CopyArtifact(source, name + Path.GetExtension(source));
        }
    }

    private sealed class Runner(IPreparedTemporaryCleanupRunner native,
        Func<TemporaryFileResult, Task>? after = null) : IPreparedTemporaryCleanupRunner
    {
        public TemporaryInventory Inventory => native.Inventory;
        public IReadOnlyList<string> CandidateIds => native.CandidateIds;
        public int Calls { get; private set; }
        public async Task<TemporaryFileResult> RunAsync(string id, CancellationToken token)
        {
            Calls++;
            var result = await native.RunAsync(id, token).ConfigureAwait(false);
            if (after is not null) await after(result).ConfigureAwait(false);
            return result;
        }
    }

    private sealed class InterceptJournal(FileTemporaryCleanupJournal inner,
        Action<TemporaryCleanupCheckpoint>? before = null,
        Func<TemporaryCleanupCheckpoint, Task>? after = null) : ITemporaryCleanupJournal
    {
        public async Task AppendAsync(TemporaryCleanupCheckpoint checkpoint, CancellationToken token)
        {
            before?.Invoke(checkpoint);
            await inner.AppendAsync(checkpoint, token).ConfigureAwait(false);
            if (after is not null) await after(checkpoint).ConfigureAwait(false);
        }
    }

    private static string OwnedDirectory(Guid id) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "WinMaintPhase8CleanupLab", id.ToString("N"));
    private static void CopyArtifact(string source, string name)
    {
        string target = Path.Combine(Destination, name);
        File.Copy(source, target, overwrite: false);
        Artifacts.Add(new(name, Hash(target)));
    }
}
