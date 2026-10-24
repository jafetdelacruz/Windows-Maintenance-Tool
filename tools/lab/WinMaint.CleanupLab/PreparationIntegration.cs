using WinMaint.Application;
using WinMaint.Domain;
using WinMaint.Infrastructure.Win;
using WinMaint.Reporting;

namespace WinMaint.CleanupLab;

internal static partial class Program
{
    private static async Task RunPreparationIntegrationAsync(string profile, string root, ReportJournal journal,
        Action<string, bool> require, CancellationToken token)
    {
        var publicPreview = await WindowsTemporaryCleanupPreparation.CreateForCurrentUserAsync(token).ConfigureAwait(false);
        var publicAttempt = await publicPreview.RunAsync(Guid.NewGuid().ToString("N"), token).ConfigureAwait(false);
        require("Public current-user preparation cannot enable production deletion",
            !publicPreview.CanExecute && publicAttempt.Outcome == TemporaryFileOutcome.BlockedScope);
        var prepared = await WindowsTemporaryCleanupPreparation.CreateForOwnedLabAsync(profile, token).ConfigureAwait(false);
        require("Prepared native inventory binds one opaque candidate to its owned root",
            prepared.CanExecute && prepared.Inventory.Coverage == EvidenceCoverage.Complete &&
            prepared.Inventory.CandidateFiles == 1 && prepared.Inventory.CandidateLogicalBytes == 18 &&
            prepared.CandidateIds.Count == 1 && Guid.TryParseExact(prepared.CandidateIds[0], "N", out _));
        journal.AddTemporaryCleanup(new(prepared.Inventory, "InventoryOnly", [], "Owned synthetic fixture only."));
        var plan = new TemporaryCleanupPlan(prepared.Inventory, true, prepared.CandidateIds);
        var coordinator = new TemporaryCleanupCoordinator(prepared, true);
        var cancelled = await coordinator.ExecuteAsync(plan, "", token).ConfigureAwait(false);
        journal.AddTemporaryCleanup(cancelled);
        require("Prepared runner cancellation leaves the candidate intact", cancelled.DeletedFiles == 0 &&
            File.Exists(Path.Combine(root, "fault-second.txt")));
        var unknown = await prepared.RunAsync(Guid.NewGuid().ToString("N"), token).ConfigureAwait(false);
        require("Unknown opaque candidate is blocked before native deletion", unknown.Outcome == TemporaryFileOutcome.BlockedScope);
        await journal.SaveAsync("InProgress", CancellationToken.None).ConfigureAwait(false);
        var result = await coordinator.ExecuteAsync(plan, plan.Confirmation, token).ConfigureAwait(false);
        journal.AddTemporaryCleanup(result);
        require("Prepared runner verifies the owned file deletion", result.DeletedFiles == 1 &&
            result.DeletedLogicalBytes == 18 && !File.Exists(Path.Combine(root, "fault-second.txt")));

        // Do not retain preparation pins: replacing an ancestor between preview and execution must be detected.
        string folder = Path.Combine(root, "prepared-parent");
        Directory.CreateDirectory(folder);
        Make(folder, "prepared-file.txt", true);
        var replaced = await WindowsTemporaryCleanupPreparation.CreateForOwnedLabAsync(profile, token).ConfigureAwait(false);
        string original = folder + "-original";
        Directory.Move(folder, original);
        Directory.CreateDirectory(folder);
        string replacement = Make(folder, "prepared-file.txt", true);
        var changedPlan = new TemporaryCleanupPlan(replaced.Inventory, true, replaced.CandidateIds);
        var changed = await new TemporaryCleanupCoordinator(replaced, true)
            .ExecuteAsync(changedPlan, changedPlan.Confirmation, token).ConfigureAwait(false);
        journal.AddTemporaryCleanup(new(replaced.Inventory, "InventoryOnly", [], "Ancestor replacement test."));
        journal.AddTemporaryCleanup(changed);
        require("Prepared runner blocks replaced directory identity and preserves both files",
            replaced.Inventory.CandidateFiles == 1 && changed.Files.Count == 1 &&
            changed.Files[0].Outcome == TemporaryFileOutcome.BlockedScope && changed.DeletedFiles == 0 &&
            File.Exists(replacement) && File.Exists(Path.Combine(original, "prepared-file.txt")));
        await journal.SaveAsync("Partial", CancellationToken.None).ConfigureAwait(false);
        var saved = ReportRenderer.Parse(await File.ReadAllTextAsync(journal.JsonPath, token).ConfigureAwait(false));
        require("Prepared runner product report preserves prior uncertainty and verified totals",
            saved.Status == "Partial" && saved.TemporaryCleanup.Count == 19 &&
            saved.TemporaryCleanup.Sum(item => item.DeletedFiles) == 6 &&
            saved.TemporaryCleanup.Sum(item => item.DeletedLogicalBytes) == 108 &&
            saved.TemporaryCleanup.SelectMany(item => item.Files).Count(f => f.Outcome == TemporaryFileOutcome.Unverified) == 1);
    }
}
