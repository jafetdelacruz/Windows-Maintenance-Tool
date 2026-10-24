using WinMaint.Application;
using WinMaint.Domain;
using WinMaint.Infrastructure.Win;
using WinMaint.Reporting;

namespace WinMaint.CleanupLab;

internal static partial class Program
{
    private static async Task RunInterruptedIntegrationAsync(string root, WindowsTemporaryInventoryProvider provider,
        ReportJournal journal, Action<string, bool> require, CancellationToken token)
    {
        string first = Make(root, "cancel-first.txt", true), second = Make(root, "cancel-second.txt", true);
        string firstId = Guid.NewGuid().ToString("N"), secondId = Guid.NewGuid().ToString("N");
        var cancelMap = new Dictionary<string, Snapshot>(StringComparer.Ordinal)
        {
            [firstId] = Capture(first),
            [secondId] = Capture(second),
        };
        journal.AddTemporaryInventory(await provider.CollectAsync(token).ConfigureAwait(false));
        var cancelInventory = journal.CreateReport("Partial").TemporaryCleanup[^1].Inventory;
        var cancelPlan = new TemporaryCleanupPlan(cancelInventory, true, [firstId, secondId]);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var cancelNative = new IntegrationRunner(root, cancelMap);
        var cancelCoordinator = new TemporaryCleanupCoordinator(new AfterDeleteRunner(cancelNative, stop, false), true);
        await journal.SaveAsync("InProgress", CancellationToken.None).ConfigureAwait(false);
        var cancelled = await cancelCoordinator.ExecuteAsync(cancelPlan, cancelPlan.Confirmation, stop.Token).ConfigureAwait(false);
        journal.AddTemporaryCleanup(cancelled);
        await journal.SaveAsync("Partial", CancellationToken.None).ConfigureAwait(false);
        require("Integrated cancellation keeps completed native deletion and preserves next file",
            cancelInventory.CandidateFiles == 2 && cancelled.Status == "Cancelled" && cancelNative.Calls == 1 &&
            cancelled.DeletedFiles == 1 && cancelled.DeletedLogicalBytes == 18 && !File.Exists(first) && File.Exists(second));
        var cancelSaved = ReportRenderer.Parse(await File.ReadAllTextAsync(journal.JsonPath, token).ConfigureAwait(false));
        require("Cancellation results survive product JSON persistence", cancelSaved.Status == "Partial" &&
            cancelSaved.TemporaryCleanup[^1].Status == "Cancelled" && cancelSaved.TemporaryCleanup[^1].DeletedFiles == 1 &&
            cancelSaved.TemporaryCleanup[^1].DeletedLogicalBytes == 18);

        // Protect the retained fixture file before the next independent inventory.
        File.SetAttributes(second, FileAttributes.ReadOnly);
        string uncertain = Make(root, "fault-first.txt", true), retained = Make(root, "fault-second.txt", true);
        string uncertainId = Guid.NewGuid().ToString("N"), retainedId = Guid.NewGuid().ToString("N");
        var faultMap = new Dictionary<string, Snapshot>(StringComparer.Ordinal)
        {
            [uncertainId] = Capture(uncertain),
            [retainedId] = Capture(retained),
        };
        journal.AddTemporaryInventory(await provider.CollectAsync(token).ConfigureAwait(false));
        var faultInventory = journal.CreateReport("Partial").TemporaryCleanup[^1].Inventory;
        var faultPlan = new TemporaryCleanupPlan(faultInventory, true, [uncertainId, retainedId]);
        var faultNative = new IntegrationRunner(root, faultMap);
        var faultCoordinator = new TemporaryCleanupCoordinator(new AfterDeleteRunner(faultNative, null, true), true);
        await journal.SaveAsync("InProgress", CancellationToken.None).ConfigureAwait(false);
        var interrupted = await faultCoordinator.ExecuteAsync(faultPlan, faultPlan.Confirmation, token).ConfigureAwait(false);
        journal.AddTemporaryCleanup(interrupted);
        await journal.SaveAsync("Partial", CancellationToken.None).ConfigureAwait(false);
        require("Post-deletion injected fault becomes Unverified instead of claiming no changes",
            faultInventory.CandidateFiles == 2 && interrupted.Status == "Interrupted" && faultNative.Calls == 1 &&
            interrupted.DeletedFiles == 0 && interrupted.Files.Count == 1 &&
            interrupted.Files[0].Outcome == TemporaryFileOutcome.Unverified &&
            interrupted.Files[0].DeletedLogicalBytes is null && !File.Exists(uncertain) && File.Exists(retained));
        journal.AddTemporaryInventory(await provider.CollectAsync(token).ConfigureAwait(false));
        await journal.SaveAsync("Partial", CancellationToken.None).ConfigureAwait(false);
        var saved = ReportRenderer.Parse(await File.ReadAllTextAsync(journal.JsonPath, token).ConfigureAwait(false));
        string text = await File.ReadAllTextAsync(journal.TextPath, token).ConfigureAwait(false);
        require("Product partial report retains verified results and explicit uncertainty",
            saved.Status == "Partial" && saved.TemporaryCleanup.Count == 14 &&
            saved.TemporaryCleanup.Sum(item => item.DeletedFiles) == 5 &&
            saved.TemporaryCleanup.Sum(item => item.DeletedLogicalBytes) == 90 &&
            saved.TemporaryCleanup.SelectMany(item => item.Files).Count(f => f.Outcome == TemporaryFileOutcome.Unverified) == 1 &&
            saved.TemporaryCleanup[^1].Inventory.CandidateFiles == 1 &&
            text.Contains("operaciones sin verificación", StringComparison.Ordinal));
    }

    // Controlled exception after a successful real deletion, not a simulated power loss.
    private sealed class AfterDeleteRunner(IntegrationRunner inner, CancellationTokenSource? stop,
        bool injectFault) : ITemporaryCleanupRunner
    {
        public async Task<TemporaryFileResult> RunAsync(string candidateId, CancellationToken cancellationToken)
        {
            var result = await inner.RunAsync(candidateId, cancellationToken).ConfigureAwait(false);
            if (result.Outcome == TemporaryFileOutcome.DeletedVerified)
            {
                if (injectFault) throw new OperationCanceledException("Injected interruption after native deletion.");
                stop?.Cancel();
            }
            return result;
        }
    }
}
