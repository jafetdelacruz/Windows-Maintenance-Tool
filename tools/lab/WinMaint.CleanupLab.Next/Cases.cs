using System.ComponentModel;
using WinMaint.Application;
using WinMaint.Domain;
using WinMaint.Infrastructure.Win;
using WinMaint.Reporting;

namespace WinMaint.CleanupLab.Next;

internal static partial class Program
{
    private static async Task RunCasesAsync(DiagnosticReport diagnostic)
    {
        // Do not run any deletion scenario until both cancellation gates pass.
        foreach (bool cancelledToken in new[] { false, true })
        {
            string name = cancelledToken ? "cancel-token" : "cancel-confirmation";
            var f = new Fixture(diagnostic);
            string path = f.Make("keep.txt");
            string before = Hash(path);
            var prepared = await f.PrepareAsync().ConfigureAwait(false);
            var runner = new Runner(prepared);
            var plan = f.Plan(runner);
            using var journal = new FileTemporaryCleanupJournal(f.JournalPath);
            using var cancellation = new CancellationTokenSource();
            if (cancelledToken) cancellation.Cancel();
            var result = await new TemporaryCleanupCoordinator(runner, true, journal: journal)
                .ExecuteAsync(plan, cancelledToken ? plan.Confirmation : "", cancellation.Token).ConfigureAwait(false);
            Require(name, result.Status == "Cancelled" && runner.Calls == 0 && Hash(path) == before &&
                !Directory.Exists(f.JournalPath));
            await f.ExportAsync(name, result).ConfigureAwait(false);
        }

        var publicPreview = await WindowsTemporaryCleanupPreparation.CreateForCurrentUserAsync().ConfigureAwait(false);
        var publicResult = await publicPreview.RunAsync(Guid.NewGuid().ToString("N"), CancellationToken.None).ConfigureAwait(false);
        Require("public-production-block", !publicPreview.CanExecute && publicResult.Outcome == TemporaryFileOutcome.BlockedScope);
        await RunExclusionsAsync(diagnostic).ConfigureAwait(false);

        foreach (string change in new[] { "busy", "changed", "identity", "symlink", "hardlink", "readonly", "parent", "root", "parent-link", "root-link", "parent-system", "native-stale", "root-pinned" })
            await RunSubstitutionAsync(diagnostic, change).ConfigureAwait(false);

        foreach (string guard in new[] { "unknown-id", "partial-plan", "expired-plan", "empty-diagnostic", "actual-partial" })
            await RunPlanGuardAsync(diagnostic, guard).ConfigureAwait(false);

        var cancelled = new Fixture(diagnostic);
        string[] paths = [cancelled.Make("first.txt"), cancelled.Make("second.txt")];
        var preparedCancel = await cancelled.PrepareAsync().ConfigureAwait(false);
        using var cts = new CancellationTokenSource();
        var cancelRunner = new Runner(preparedCancel, result =>
        { if (result.Outcome == TemporaryFileOutcome.DeletedVerified) cts.Cancel(); return Task.CompletedTask; });
        using (var journal = new FileTemporaryCleanupJournal(cancelled.JournalPath))
        {
            var plan = cancelled.Plan(cancelRunner);
            var result = await new TemporaryCleanupCoordinator(cancelRunner, true, journal: journal)
                .ExecuteAsync(plan, plan.Confirmation, cts.Token).ConfigureAwait(false);
            Require("mid-cancellation", result.Status == "Cancelled" && cancelRunner.Calls == 1 && result.DeletedFiles == 1 &&
                result.DeletedLogicalBytes == 18 && result.NotAttemptedCandidates.Count == 1 && paths.Count(Missing) == 1);
            await cancelled.ExportAsync("mid-cancellation", result).ConfigureAwait(false);
        }

        var uncertain = new Fixture(diagnostic);
        string removed = uncertain.Make("removed.txt");
        var preparedUncertain = await uncertain.PrepareAsync().ConfigureAwait(false);
        var faultRunner = new Runner(preparedUncertain, _ => throw new IOException("Injected after actual native deletion/verification, before returning to coordinator."));
        using (var journal = new FileTemporaryCleanupJournal(uncertain.JournalPath))
        {
            var plan = uncertain.Plan(faultRunner);
            var result = await new TemporaryCleanupCoordinator(faultRunner, true, journal: journal)
                .ExecuteAsync(plan, plan.Confirmation).ConfigureAwait(false);
            Require("error-after-deletion", result.Status == "Failed" && faultRunner.Calls == 1 && result.DeletedFiles == 0 &&
                result.Files.Single().Outcome == TemporaryFileOutcome.Unverified && Missing(removed));
            await uncertain.ExportAsync("error-after-deletion", result).ConfigureAwait(false);
        }
        foreach (int sequence in new[] { 0, 1, 2, 3, 4, 5 })
            await RunPersistenceFailureAsync(diagnostic, sequence).ConfigureAwait(false);

        var late = new Fixture(diagnostic);
        string[] latePaths = [late.Make("first.txt"), late.Make("second.txt")];
        await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        var latePrepared = await late.PrepareAsync().ConfigureAwait(false);
        var lateRunner = new Runner(latePrepared);
        var latePlan = late.Plan(lateRunner);
        var simulatedNow = diagnostic.CapturedAt.AddMinutes(5).AddSeconds(-1);
        using (var store = new FileTemporaryCleanupJournal(late.JournalPath))
        {
            var journal = new InterceptJournal(store, after: checkpoint =>
            {
                if (checkpoint.Kind == TemporaryCleanupCheckpointKind.Intent) simulatedNow = simulatedNow.AddSeconds(2);
                return Task.CompletedTask;
            });
            var result = await new TemporaryCleanupCoordinator(lateRunner, true, () => simulatedNow, journal)
                .ExecuteAsync(latePlan, latePlan.Confirmation).ConfigureAwait(false);
            Require("late-diagnostic", result.Status == "Interrupted" && lateRunner.Calls == 0 && latePaths.All(path => !Missing(path)) &&
                result.Files.Single().Outcome == TemporaryFileOutcome.BlockedStale && result.NotAttemptedCandidates.Count == 1 &&
                simulatedNow - latePrepared.Inventory.CapturedAt < TimeSpan.FromMinutes(5));
            await late.ExportAsync("late-diagnostic", result).ConfigureAwait(false);
        }
    }

    private static async Task RunExclusionsAsync(DiagnosticReport diagnostic)
    {
        var f = new Fixture(diagnostic);
        string first = f.Make("old.txt"), nested = f.Make(@"nested\old.txt"), empty = f.Make("zero.txt", content: "");
        var protectedFiles = new List<string> { f.Make("recent.txt", false), f.Make("recent-write.txt"), f.Make("recent-creation.txt"),
            f.Make("readonly.txt"), f.Make("system.txt"), f.Make(@".net\keep.txt"), f.Make(@"WinMaintExcluded\keep.txt") };
        File.SetLastWriteTimeUtc(protectedFiles[1], DateTime.UtcNow);
        File.SetCreationTimeUtc(protectedFiles[2], DateTime.UtcNow);
        File.SetAttributes(protectedFiles[3], FileAttributes.ReadOnly);
        File.SetAttributes(protectedFiles[4], FileAttributes.System);
        string outside = Path.Combine(f.Owned, "outside.txt");
        File.WriteAllText(outside, "OUTSIDE SENTINEL");
        protectedFiles.Add(outside);
        string outsideDir = Path.Combine(f.Owned, "outside-directory");
        Directory.CreateDirectory(outsideDir);
        string outsideNested = Path.Combine(outsideDir, "keep.txt");
        File.WriteAllText(outsideNested, "OUTSIDE DIRECTORY SENTINEL");
        protectedFiles.Add(outsideNested);
        File.CreateSymbolicLink(Path.Combine(f.Root, "file-link.txt"), outside);
        Directory.CreateSymbolicLink(Path.Combine(f.Root, "directory-link"), outsideDir);
        WindowsTemporaryNative.AddHardLink(Path.Combine(f.Root, "hardlink.txt"), outside);
        var hashes = protectedFiles.ToDictionary(path => path, Hash, StringComparer.Ordinal);
        var prepared = await f.PrepareAsync().ConfigureAwait(false);
        Require("exclusions/prepared", prepared.CanExecute && prepared.Inventory.CandidateFiles == 3 &&
            prepared.Inventory.CandidateLogicalBytes == 36 && prepared.Inventory.RecentFiles == 3 &&
            prepared.Inventory.ProtectedItems == 7 && prepared.Inventory.UnavailableItems == 0);
        using var journal = new FileTemporaryCleanupJournal(f.JournalPath);
        var runner = new Runner(prepared);
        var plan = f.Plan(runner);
        var result = await new TemporaryCleanupCoordinator(runner, true, journal: journal).ExecuteAsync(plan, plan.Confirmation).ConfigureAwait(false);
        Require("exclusions/native-effects", result.Status == "Completed" && runner.Calls == 3 && result.DeletedFiles == 3 &&
            result.DeletedLogicalBytes == 36 && new[] { first, nested, empty }.All(Missing) &&
            hashes.All(pair => Hash(pair.Key) == pair.Value) && Directory.Exists(Path.GetDirectoryName(nested)) &&
            (File.GetAttributes(Path.Combine(f.Root, "file-link.txt")) & FileAttributes.ReparsePoint) != 0 &&
            (File.GetAttributes(Path.Combine(f.Root, "directory-link")) & FileAttributes.ReparsePoint) != 0);
        await f.ExportAsync("exclusions", result).ConfigureAwait(false);
    }

    private static async Task RunSubstitutionAsync(DiagnosticReport diagnostic, string kind)
    {
        string name = "substitution-" + kind;
        var f = new Fixture(diagnostic);
        string relative = kind is "parent" or "parent-link" or "parent-system" ? @"parent\old.txt" : "old.txt";
        string path = f.Make(relative);
        string originalHash = Hash(path);
        string outside = Path.Combine(f.Owned, "outside.txt");
        File.WriteAllText(outside, "OUTSIDE SENTINEL");
        string outsideHash = Hash(outside);
        bool renameBlocked = false;
        Action? pinned = kind == "root-pinned" ? () =>
        {
            try { Directory.Move(f.Root, f.Root + "-moved"); }
            catch (IOException) { renameBlocked = true; }
        }
        : null;
        var prepared = await f.PrepareAsync(new(pinned, kind == "native-stale" ? TimeSpan.FromMinutes(6) : null)).ConfigureAwait(false);
        Require(name + "/prepared", prepared.Inventory.CandidateFiles == 1 && prepared.CanExecute);
        string? preservedOriginal = null;
        FileStream? busy = null;
        switch (kind)
        {
            case "busy": busy = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None); break;
            case "changed": File.AppendAllText(path, "CHANGED"); break;
            case "identity":
                string replacement = f.Make("replacement.txt");
                File.SetCreationTimeUtc(replacement, File.GetCreationTimeUtc(path));
                File.SetLastWriteTimeUtc(replacement, File.GetLastWriteTimeUtc(path));
                File.Move(replacement, path, overwrite: true);
                break;
            case "symlink": File.Delete(path); File.CreateSymbolicLink(path, outside); break;
            case "hardlink": WindowsTemporaryNative.AddHardLink(Path.Combine(f.Owned, "new-hardlink.txt"), path); break;
            case "readonly": File.SetAttributes(path, FileAttributes.ReadOnly); break;
            case "parent":
            case "root":
            case "parent-link":
            case "root-link":
                string directory = kind is "root" or "root-link" ? f.Root : Path.GetDirectoryName(path)!;
                Directory.Move(directory, directory + "-original");
                preservedOriginal = Path.Combine(directory + "-original", Path.GetFileName(path));
                if (kind.EndsWith("-link", StringComparison.Ordinal))
                {
                    string outsideDirectory = Path.Combine(f.Owned, "foreign-parent");
                    Directory.CreateDirectory(outsideDirectory);
                    File.Copy(outside, Path.Combine(outsideDirectory, "old.txt"));
                    Directory.CreateSymbolicLink(directory, outsideDirectory);
                }
                else _ = f.Make(relative);
                break;
            case "parent-system": File.SetAttributes(Path.GetDirectoryName(path)!, FileAttributes.Directory | FileAttributes.System); break;
        }
        TemporaryCleanupRecord result;
        var runner = new Runner(prepared);
        using (var journal = new FileTemporaryCleanupJournal(f.JournalPath))
        {
            var plan = f.Plan(runner);
            result = await new TemporaryCleanupCoordinator(runner, true, journal: journal).ExecuteAsync(plan, plan.Confirmation).ConfigureAwait(false);
        }
        busy?.Dispose();
        var expected = kind switch
        {
            "busy" => TemporaryFileOutcome.SkippedBusy,
            "root" or "parent" or "parent-link" or "root-link" or "parent-system" => TemporaryFileOutcome.BlockedScope,
            "native-stale" => TemporaryFileOutcome.BlockedStale,
            "root-pinned" => TemporaryFileOutcome.DeletedVerified,
            _ => TemporaryFileOutcome.SkippedChanged,
        };
        Require(name + "/native-effects", runner.Calls == 1 && result.Files.Single().Outcome == expected &&
            result.DeletedFiles == (kind == "root-pinned" ? 1 : 0) && Hash(outside) == outsideHash &&
            (kind == "root-pinned" ? renameBlocked && Missing(path) : !Missing(path)) &&
            (preservedOriginal is null || Hash(preservedOriginal) == originalHash));
        await f.ExportAsync(name, result).ConfigureAwait(false);
    }

    private static async Task RunPlanGuardAsync(DiagnosticReport diagnostic, string kind)
    {
        string name = "guard-" + kind;
        var f = new Fixture(diagnostic);
        string path = f.Make("keep.txt");
        string before = Hash(path);
        if (kind == "actual-partial")
        {
            string relative = string.Join(Path.DirectorySeparatorChar, Enumerable.Repeat("deep", 34)) + "\\deep.txt";
            _ = f.Make(relative);
        }
        var prepared = await f.PrepareAsync().ConfigureAwait(false);
        var runner = new Runner(prepared);
        var plan = f.Plan(runner);
        if (kind == "unknown-id") plan = plan with { CandidateIds = [Guid.NewGuid().ToString("N")] };
        if (kind == "partial-plan") plan = plan with { Inventory = plan.Inventory with { Coverage = EvidenceCoverage.Partial } };
        if (kind == "expired-plan") plan = plan with { Inventory = plan.Inventory with { CapturedAt = DateTimeOffset.UtcNow.AddMinutes(-6) } };
        if (kind == "empty-diagnostic") plan = plan with
        { DiagnosticEvidence = plan.DiagnosticEvidence! with { Snapshot = new(diagnostic.CapturedAt, [], true) } };
        using var journal = new FileTemporaryCleanupJournal(f.JournalPath);
        var result = await new TemporaryCleanupCoordinator(runner, true, journal: journal).ExecuteAsync(plan, plan.Confirmation).ConfigureAwait(false);
        Require(name, result.Status == "Blocked" && runner.Calls == 0 && Hash(path) == before && !Directory.Exists(f.JournalPath) &&
            (kind != "actual-partial" || prepared.Inventory.Coverage == EvidenceCoverage.Partial));
        if (kind == "unknown-id")
            Require(name + "/native-map", (await prepared.RunAsync(plan.CandidateIds[0], CancellationToken.None).ConfigureAwait(false)).Outcome == TemporaryFileOutcome.BlockedScope);
        await f.ExportAsync(name, result).ConfigureAwait(false);
    }

    private static async Task RunPersistenceFailureAsync(DiagnosticReport diagnostic, int sequence)
    {
        string name = "persistence-failure-" + sequence;
        var f = new Fixture(diagnostic);
        string[] paths = [f.Make("first.txt"), f.Make("second.txt")];
        var prepared = await f.PrepareAsync().ConfigureAwait(false);
        if (sequence == 0) File.WriteAllText(f.JournalPath, "A file cannot be used as a journal directory.");
        using var store = new FileTemporaryCleanupJournal(f.JournalPath);
        var journal = new InterceptJournal(store, before: checkpoint =>
        {
            if (checkpoint.Sequence == sequence && sequence != 0)
            {
                string folder = Path.Combine(f.JournalPath, checkpoint.AttemptId.ToString("N"));
                File.WriteAllText(Path.Combine(folder, checkpoint.Sequence.ToString("D8", System.Globalization.CultureInfo.InvariantCulture) + ".json"), "{injected-collision");
            }
        });
        var runner = new Runner(prepared);
        var plan = f.Plan(runner);
        var result = await new TemporaryCleanupCoordinator(runner, true, journal: journal).ExecuteAsync(plan, plan.Confirmation).ConfigureAwait(false);
        Require(name + "/effects", result.Status == (sequence == 0 ? "Blocked" : "Interrupted") &&
            runner.Calls == sequence / 2 && paths.Count(Missing) == sequence / 2 &&
            result.DeletedFiles == (sequence == 0 ? 0 : (sequence - 1) / 2) &&
            result.NotAttemptedCandidates.Count == 2 - sequence / 2);
        if (sequence > 0)
        {
            using var reopened = new FileTemporaryCleanupJournal(f.JournalPath);
            var recovered = (await reopened.ReadAllAsync().ConfigureAwait(false)).Single();
            Require(name + "/recovery", !recovered.IsClosed && recovered.IsCorrupt &&
                recovered.Files.Count == 2 && recovered.NotAttempted.Count == 0 &&
                recovered.ToRecord()!.DeletedFiles == (sequence - 1) / 2 &&
                recovered.Files.Count(file => file.Outcome == TemporaryFileOutcome.Unverified) == 2 - (sequence - 1) / 2);
        }
        await f.ExportAsync(name, result).ConfigureAwait(false);
    }
}
