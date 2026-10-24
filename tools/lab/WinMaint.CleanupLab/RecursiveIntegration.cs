using System.Diagnostics;
using Microsoft.Win32.SafeHandles;
using WinMaint.Application;
using WinMaint.Domain;
using WinMaint.Infrastructure.Win;
using WinMaint.Reporting;

namespace WinMaint.CleanupLab;

internal static partial class Program
{
    private static async Task RunRecursiveIntegrationAsync(string root, string owned,
        WindowsTemporaryInventoryProvider provider, ReportJournal journal, List<SafeFileHandle> pins,
        Action<string, bool> require, CancellationToken token)
    {
        string a = Path.Combine(root, "nested-a"), b = Path.Combine(a, "nested-b");
        string dotnet = Path.Combine(root, ".net"), protectedFolder = Path.Combine(root, "WinMaint-protected");
        foreach (string folder in new[] { a, b, dotnet, protectedFolder }) Directory.CreateDirectory(folder);
        string first = Make(a, "nested-old-a.txt", true), second = Make(b, "nested-old-b.txt", true);
        string recent = Make(b, "nested-recent.txt", false);
        string runtime = Make(dotnet, "runtime-old.txt", true), ownFile = Make(protectedFolder, "protected-old.txt", true);
        string linkedFolder = Path.Combine(root, "nested-outside-link");
        Directory.CreateSymbolicLink(linkedFolder, owned);
        var before = await provider.CollectAsync(token).ConfigureAwait(false);
        journal.AddTemporaryInventory(before);
        var inventory = journal.CreateReport("Partial").TemporaryCleanup[^1].Inventory;
        var candidates = CollectRecursiveCandidates(root, pins, token);
        require("Recursive inventory and native metadata map agree", inventory.Coverage == EvidenceCoverage.Complete &&
            inventory.CandidateFiles == 3 && inventory.CandidateLogicalBytes == 54 && inventory.RecentFiles == 3 &&
            inventory.ProtectedItems == 5 && candidates.Count == 3 &&
            candidates.Values.Sum(s => checked((long)s.Identity.Size)) == inventory.CandidateLogicalBytes);
        var plan = new TemporaryCleanupPlan(inventory, true, candidates.Keys.ToArray());
        var coordinator = new TemporaryCleanupCoordinator(new IntegrationRunner(root, candidates, recursive: true), true);
        await journal.SaveAsync("InProgress", CancellationToken.None).ConfigureAwait(false);
        var result = await coordinator.ExecuteAsync(plan, plan.Confirmation, token).ConfigureAwait(false);
        journal.AddTemporaryCleanup(result);
        // Persist already completed mutations even when the operator cancels.
        await journal.SaveAsync("Partial", CancellationToken.None).ConfigureAwait(false);
        require("Scoped native bridge deletes three eligible files across nested directories", result.Status == "Completed" &&
            result.DeletedFiles == 3 && result.DeletedLogicalBytes == 54 && !File.Exists(first) && !File.Exists(second));
        require("Recent, runtime, protected folder and linked outside target survive",
            new[] { recent, runtime, ownFile }.All(File.Exists) && Directory.Exists(linkedFolder) &&
            File.ReadAllText(Path.Combine(owned, "integration-outside.txt")) == "OUTSIDE SENTINEL");
        require("Traversed nested directory identities stay pinned", RootRenameBlocked(a) && RootRenameBlocked(b));
        journal.AddTemporaryInventory(await provider.CollectAsync(token).ConfigureAwait(false));
        require("Recursive verification finds no remaining eligible files and preserves folders",
            journal.CreateReport("Partial").TemporaryCleanup[^1].Inventory.CandidateFiles == 0 &&
            new[] { root, a, b, dotnet, protectedFolder }.All(Directory.Exists));
        await journal.SaveAsync("Completed", CancellationToken.None).ConfigureAwait(false);
        var parsed = ReportRenderer.Parse(await File.ReadAllTextAsync(journal.JsonPath, token).ConfigureAwait(false));
        require("Product report preserves all flat and recursive results", parsed.TemporaryCleanup.Count == 9 &&
            parsed.TemporaryCleanup.Sum(item => item.DeletedFiles) == 4 &&
            parsed.TemporaryCleanup.Sum(item => item.DeletedLogicalBytes) == 72);
    }

    private static Dictionary<string, Snapshot> CollectRecursiveCandidates(string root,
        List<SafeFileHandle> pins, CancellationToken token)
    {
        var result = new Dictionary<string, Snapshot>(StringComparer.Ordinal);
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((root, 0));
        var clock = Stopwatch.StartNew();
        int visited = 0;
        while (pending.TryPop(out var folder))
        {
            token.ThrowIfCancellationRequested();
            foreach (string entry in Directory.EnumerateFileSystemEntries(folder.Path))
            {
                token.ThrowIfCancellationRequested();
                if (++visited > 20_000 || clock.Elapsed > TimeSpan.FromSeconds(15))
                    throw new InvalidOperationException("Candidate traversal exceeded its safety budget; no execution authorized.");
                if (!entry.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Candidate traversal escaped the owned root.");
                var attributes = File.GetAttributes(entry);
                if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.System | FileAttributes.Offline)) != 0) continue;
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (WindowsTemporaryInventoryProvider.IsProtectedFolder(Path.GetFileName(entry))) continue;
                    if (folder.Depth >= 32) throw new InvalidOperationException("Candidate traversal exceeded depth limit.");
                    Pin(entry, pins);
                    pending.Push((entry, folder.Depth + 1));
                }
                else
                {
                    var snapshot = Capture(entry);
                    if (NativeFiles.Eligible(snapshot.Identity, DateTime.UtcNow - WindowsTemporaryInventoryProvider.MinimumAge))
                        result.Add(Guid.NewGuid().ToString("N"), snapshot);
                }
            }
        }
        return result;
    }
}
