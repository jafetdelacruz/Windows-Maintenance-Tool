using System.Diagnostics;
using System.Text;
using WinMaint.Application;
using WinMaint.Domain;
using WinMaint.Infrastructure.Win;
using WinMaint.Reporting;

namespace WinMaint.CleanupLab.Next;

internal static partial class Program
{
    private static async Task<int> RunCrashChildAsync(Guid id, string boundary)
    {
        if (boundary is not ("intent" or "after-deletion" or "outcome")) return 3;
        var diagnostic = await WindowsDiagnosticComposition.Create().CollectAsync().ConfigureAwait(false);
        var f = new Fixture(diagnostic, id);
        _ = f.Make("first.txt");
        _ = f.Make("second.txt");
        var prepared = await f.PrepareAsync().ConfigureAwait(false);
        if (!prepared.CanExecute || prepared.Inventory.CandidateFiles != 2)
            throw new InvalidOperationException("Crash fixture preparation failed.");
        async Task WaitForOwnedParentAsync()
        {
            await File.WriteAllTextAsync(Path.Combine(f.Owned, "ready"), boundary).ConfigureAwait(false);
            await Task.Delay(Timeout.InfiniteTimeSpan).ConfigureAwait(false);
        }
        var runner = new Runner(prepared, result => boundary == "after-deletion" && result.Outcome == TemporaryFileOutcome.DeletedVerified
            ? WaitForOwnedParentAsync() : Task.CompletedTask);
        var plan = f.Plan(runner);
        using var store = new FileTemporaryCleanupJournal(f.JournalPath);
        var journal = new InterceptJournal(store, after: checkpoint =>
            boundary == "intent" && checkpoint.Kind == TemporaryCleanupCheckpointKind.Intent ||
            boundary == "outcome" && checkpoint.Kind == TemporaryCleanupCheckpointKind.Outcome
                ? WaitForOwnedParentAsync() : Task.CompletedTask);
        _ = await new TemporaryCleanupCoordinator(runner, true, journal: journal)
            .ExecuteAsync(plan, plan.Confirmation).ConfigureAwait(false);
        throw new InvalidOperationException("Child returned without reaching its owned interruption boundary.");
    }

    private static async Task RunCrashParentAsync(string boundary)
    {
        var id = Guid.NewGuid();
        string owned = OwnedDirectory(id);
        string ready = Path.Combine(owned, "ready");
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in new[] { "--crash-child", id.ToString("N"), boundary }) start.ArgumentList.Add(argument);
        using var child = Process.Start(start) ?? throw new IOException("Could not start owned crash child.");
        var stdout = child.StandardOutput.ReadToEndAsync();
        var stderr = child.StandardError.ReadToEndAsync();
        bool reached = false;
        try
        {
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed < TimeSpan.FromSeconds(60) && !child.HasExited)
            {
                if (File.Exists(ready)) { reached = true; break; }
                await Task.Delay(100).ConfigureAwait(false);
            }
        }
        finally
        {
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true); // Only the Process handle created above, never a PID search.
                await child.WaitForExitAsync().ConfigureAwait(false);
            }
        }
        var output = await stdout.ConfigureAwait(false);
        var error = await stderr.ConfigureAwait(false);
        if (!reached) throw new InvalidOperationException("Owned crash boundary not reached: " + boundary + "; " + output + error);
        Require("crash-" + boundary + "/terminated-owned-child", child.HasExited);

        using var journal = new FileTemporaryCleanupJournal(Path.Combine(owned, "journal"));
        var recovery = (await journal.ReadAllAsync().ConfigureAwait(false)).Single();
        string root = Path.Combine(owned, "synthetic-profile", "AppData", "Local", "Temp");
        string[] paths = [Path.Combine(root, "first.txt"), Path.Combine(root, "second.txt")];
        int expectedRemoved = boundary == "intent" ? 0 : 1;
        Require("crash-" + boundary + "/physical-state", paths.Count(Missing) == expectedRemoved && Directory.Exists(root));
        Require("crash-" + boundary + "/recovery", !recovery.IsClosed && !recovery.IsCorrupt &&
            recovery.Files.Count == 1 && recovery.NotAttempted.Count == 1 &&
            recovery.Files.Single().Outcome == (boundary == "outcome" ? TemporaryFileOutcome.DeletedVerified : TemporaryFileOutcome.Unverified) &&
            recovery.ToRecord()!.DeletedFiles == (boundary == "outcome" ? 1 : 0));
        string Fingerprint() => string.Join(";", paths.Select(path => Missing(path) ? "absent" : Hash(path)));
        string before = Fingerprint();
        _ = await journal.ReadAllAsync().ConfigureAwait(false);
        Require("crash-" + boundary + "/no-resumption", Fingerprint() == before);

        string originalPath = Directory.GetFiles(Path.Combine(owned, "reports"), "*.json").Single();
        var original = ReportRenderer.Parse(await File.ReadAllTextAsync(originalPath).ConfigureAwait(false));
        Require("crash-" + boundary + "/original-diagnostic-and-inventory", original.Snapshots.Count == 1 &&
            original.TemporaryCleanup.Single().Inventory == recovery.Inventory && original.Status == "InProgress" &&
            recovery.DiagnosticSessionId == original.SessionId && recovery.DiagnosticCapturedAt == original.Snapshots[0].Diagnostic.CapturedAt);
        var recovered = original with
        {
            Status = "Partial",
            UpdatedAt = DateTimeOffset.UtcNow,
            ExportId = Guid.NewGuid(),
            TemporaryCleanup = [.. original.TemporaryCleanup, recovery.ToRecord()!],
        };
        string json = ReportRenderer.ToJson(recovered), text = ReportRenderer.ToText(recovered);
        string jsonName = "crash-" + boundary + ".json", textName = "crash-" + boundary + ".txt";
        await File.WriteAllTextAsync(Path.Combine(Destination, jsonName), json, new UTF8Encoding(false)).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(Destination, textName), text, new UTF8Encoding(false)).ConfigureAwait(false);
        Artifacts.Add(new(jsonName, Hash(Path.Combine(Destination, jsonName))));
        Artifacts.Add(new(textName, Hash(Path.Combine(Destination, textName))));
        Require("crash-" + boundary + "/product-report", ReportRenderer.IsMatchingExport(json, text) &&
            text.Contains("no intentados: 1", StringComparison.Ordinal) &&
            (boundary == "outcome" || text.Contains("sin verificación", StringComparison.Ordinal)));
        foreach (string checkpoint in Directory.GetFiles(Path.Combine(owned, "journal", recovery.AttemptId.ToString("N"))))
            CopyArtifact(checkpoint, "crash-" + boundary + "-checkpoint-" + Path.GetFileName(checkpoint));
    }
}
