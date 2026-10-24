using System.Globalization;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using WinMaint.Application;
using WinMaint.Domain;
using WinMaint.Infrastructure.Win;
using WinMaint.Reporting;

namespace WinMaint.CleanupLab;

internal static partial class Program
{
    private static ReportJournal? integrationJournal;

    private static void ExportIntegrationFailure(Exception error)
    {
        try
        {
            using var stream = new FileStream(Path.Combine(Output, "integration-error-" + BatchId + ".json"), FileMode.CreateNew);
            JsonSerializer.Serialize(stream, new
            {
                SchemaVersion = "integration-lab-1.0",
                LabVersion = "0.8.0",
                BatchId,
                ProductionEnabled = false,
                Passed = false,
                ErrorType = error.GetType().Name,
                Error = error.Message,
            }, JsonOptions);
            if (integrationJournal is not null)
            {
                foreach (string path in new[] { integrationJournal.JsonPath, integrationJournal.TextPath })
                {
                    string target = Path.Combine(Output, Path.GetFileName(path));
                    if (File.Exists(path) && !File.Exists(target)) File.Copy(path, target, overwrite: false);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine("Failure export unavailable: " + ex.GetType().Name);
        }
    }

    private static async Task<int> RunIntegrationAsync()
    {
        Console.WriteLine("INTEGRATION LAB: real product inventory, coordinator, native lab runner and reports.");
        string owned = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Temp", "WinMaintPhase8CleanupLab", Guid.NewGuid().ToString("N"));
        string profile = Path.Combine(owned, "synthetic-profile");
        string root = Path.Combine(profile, "AppData", "Local", "Temp");
        var pins = new List<SafeFileHandle>();
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += handler;
        try
        {
            string current = Path.GetPathRoot(root)!;
            Pin(current, pins);
            Directory.CreateDirectory(Reports);
            Pin(Reports, pins);
            foreach (string component in root[current.Length..].Split(Path.DirectorySeparatorChar))
            {
                current = Path.Combine(current, component);
                Directory.CreateDirectory(current);
                Pin(current, pins);
            }
            if (!RootRenameBlocked(root)) throw new InvalidOperationException("Integration root guard failed before deletion.");
            string old = Make(root, "integration-old.txt", true);
            string busy = Make(root, "integration-busy.txt", true);
            string changed = Make(root, "integration-changed.txt", true);
            string recent = Make(root, "integration-recent.txt", false);
            string readOnly = Make(root, "integration-readonly.txt", true);
            File.SetAttributes(readOnly, FileAttributes.ReadOnly);
            string outside = Path.Combine(owned, "integration-outside.txt");
            File.WriteAllText(outside, "OUTSIDE SENTINEL");
            string link = Path.Combine(root, "integration-link.txt");
            File.CreateSymbolicLink(link, outside);

            var diagnostic = await WindowsDiagnosticComposition.Create().CollectAsync(cancellation.Token).ConfigureAwait(false);
            var journal = new ReportJournal(Path.Combine(Reports, "integration-" + BatchId));
            integrationJournal = journal;
            journal.AddSnapshot(diagnostic);
            journal.AddNote("Synthetic Sandbox integration lab 0.8.0; prepared opaque candidates and ancestor identity guards. Production cleanup remains blocked. Batch " + BatchId);
            var provider = new WindowsTemporaryInventoryProvider(profile, root);
            var section = await provider.CollectAsync(cancellation.Token).ConfigureAwait(false);
            journal.AddTemporaryInventory(section);
            var inventory = journal.CreateReport("Partial").TemporaryCleanup.Single().Inventory;
            var candidates = new Dictionary<string, Snapshot>(StringComparer.Ordinal);
            // Flat, owned fixture only: obtain candidates from metadata, not a hardcoded file list.
            foreach (string path in Directory.EnumerateFiles(root))
            {
                var snapshot = Capture(path);
                if (NativeFiles.Eligible(snapshot.Identity, DateTime.UtcNow.AddDays(-7)))
                    candidates.Add(Guid.NewGuid().ToString("N"), snapshot);
            }
            var checks = new List<Check>();
            void Require(string name, bool passed)
            {
                checks.Add(new(name, passed));
                Console.WriteLine($"{(passed ? "PASS" : "FAIL")}: {name}");
                if (!passed) throw new InvalidOperationException(name);
            }
            Require("Product inventory complete with three candidates", inventory.Coverage == EvidenceCoverage.Complete &&
                inventory.CandidateFiles == 3 && inventory.CandidateLogicalBytes == 54 && inventory.RecentFiles == 1 &&
                inventory.ProtectedItems == 2 && inventory.UnavailableItems == 0);
            bool hasDiagnostic = diagnostic.IsReadOnly && diagnostic.Sections.Any(s => s.Coverage != EvidenceCoverage.Absent);
            Require("Actual Windows read-only diagnostic collected", hasDiagnostic);
            var plan = new TemporaryCleanupPlan(inventory, hasDiagnostic, candidates.Keys.ToArray());
            var runner = new IntegrationRunner(root, candidates);
            var coordinator = new TemporaryCleanupCoordinator(runner, executionEnabled: true);
            var blocked = await new TemporaryCleanupCoordinator(runner).ExecuteAsync(plan, plan.Confirmation).ConfigureAwait(false);
            journal.AddTemporaryCleanup(blocked);
            Require("Production-disabled coordinator does not invoke native runner", blocked.Status == "Blocked" && runner.Calls == 0);
            var noDiagnostic = await coordinator.ExecuteAsync(plan with { HasDiagnostic = false }, plan.Confirmation).ConfigureAwait(false);
            journal.AddTemporaryCleanup(noDiagnostic);
            Require("Missing diagnostic blocks native runner", noDiagnostic.Status == "Blocked" && runner.Calls == 0);
            var cancelled = await coordinator.ExecuteAsync(plan, string.Empty, cancellation.Token).ConfigureAwait(false);
            journal.AddTemporaryCleanup(cancelled);
            Require("Wrong confirmation preserves all fixture files", cancelled.Status == "Cancelled" && runner.Calls == 0 &&
                new[] { old, busy, changed, recent, readOnly, link }.All(File.Exists));
            await journal.SaveAsync("InProgress").ConfigureAwait(false);
            // Deliberately change one preview candidate; lock another during execution.
            File.AppendAllText(changed, "CHANGED AFTER PREVIEW");
            TemporaryCleanupRecord executed;
            using (var held = new FileStream(busy, FileMode.Open, FileAccess.Read, FileShare.None))
                executed = await coordinator.ExecuteAsync(plan, plan.Confirmation, cancellation.Token).ConfigureAwait(false);
            journal.AddTemporaryCleanup(executed);
            await journal.SaveAsync("Partial").ConfigureAwait(false);
            Require("Native bridge reports one verified deletion and two skips", executed.Status == "FinishedWithSkips" &&
                executed.DeletedFiles == 1 && executed.DeletedLogicalBytes == 18 && executed.Files.Count == 3 &&
                executed.Files.Count(file => file.Outcome == TemporaryFileOutcome.SkippedBusy && file.NativeError == 32) == 1 &&
                executed.Files.Count(file => file.Outcome == TemporaryFileOutcome.SkippedChanged) == 1);
            Require("Protected and changed objects survive actual execution", !File.Exists(old) &&
                new[] { busy, changed, recent, readOnly, link }.All(File.Exists) && File.ReadAllText(outside) == "OUTSIDE SENTINEL");
            var after = await provider.CollectAsync(cancellation.Token).ConfigureAwait(false);
            journal.AddTemporaryInventory(after);
            Require("Post-execution product inventory sees one remaining candidate",
                after.Data.Single(d => d.MetricKey == "temp.preview.files.count").NumericValue == 1);
            await journal.SaveAsync("Completed").ConfigureAwait(false);
            var saved = ReportRenderer.Parse(await File.ReadAllTextAsync(journal.JsonPath).ConfigureAwait(false));
            Require("Product JSON 1.1 preserves inventory, guards and native results", saved.SchemaVersion == "1.1" &&
                saved.Status == "Completed" && saved.Snapshots.Count == 1 && saved.TemporaryCleanup.Count == 6 &&
                saved.TemporaryCleanup.Sum(item => item.DeletedFiles) == 1);
            string text = await File.ReadAllTextAsync(journal.TextPath).ConfigureAwait(false);
            Require("Product TXT distinguishes verified deletion and no physical-space claim",
                text.Contains("1 eliminaciones verificadas", StringComparison.Ordinal) && text.Contains("Espacio físico no medido", StringComparison.Ordinal));

            await RunRecursiveIntegrationAsync(root, owned, provider, journal, pins, Require, cancellation.Token).ConfigureAwait(false);
            await RunInterruptedIntegrationAsync(root, provider, journal, Require, cancellation.Token).ConfigureAwait(false);
            await RunPreparationIntegrationAsync(profile, root, journal, Require, cancellation.Token).ConfigureAwait(false);

            File.Copy(journal.JsonPath, Path.Combine(Output, Path.GetFileName(journal.JsonPath)), overwrite: false);
            File.Copy(journal.TextPath, Path.Combine(Output, Path.GetFileName(journal.TextPath)), overwrite: false);
            string manifest = JsonSerializer.Serialize(new
            {
                SchemaVersion = "integration-lab-1.0",
                LabVersion = "0.8.0",
                BatchId,
                ProductionEnabled = false,
                Passed = checks.All(c => c.Passed),
                Checks = checks,
                ProductReport = Path.GetFileName(journal.JsonPath),
            }, JsonOptions);
            using (var stream = new FileStream(Path.Combine(Output, "integration-" + BatchId + ".json"), FileMode.CreateNew))
            using (var writer = new StreamWriter(stream)) await writer.WriteAsync(manifest).ConfigureAwait(false);
            using var marker = new FileStream(Marker, FileMode.CreateNew);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"INTEGRATION LAB PASSED: {checks.Count}/{checks.Count}. Product reports exported. Production blocked."));
            Console.ResetColor();
            return 0;
        }
        finally
        {
            Console.CancelKeyPress -= handler;
            foreach (var pin in Enumerable.Reverse(pins)) pin.Dispose();
        }
    }

    private sealed class IntegrationRunner(string root, IReadOnlyDictionary<string, Snapshot> candidates,
        bool recursive = false) : ITemporaryCleanupRunner
    {
        public int Calls { get; private set; }
        public Task<TemporaryFileResult> RunAsync(string candidateId, CancellationToken cancellationToken)
        {
            Calls++;
            if (!candidates.TryGetValue(candidateId, out var snapshot))
                return Task.FromResult(new TemporaryFileResult(candidateId, TemporaryFileOutcome.BlockedScope, 0));
            string? parent = Path.GetDirectoryName(snapshot.Path);
            // Recursive maps are collected while every traversed directory is pinned.
            if (parent is null || !snapshot.Path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(new TemporaryFileResult(candidateId, TemporaryFileOutcome.BlockedScope, 0));
            var result = DeleteOne(snapshot, recursive ? parent : root, cancellationToken);
            var outcome = result.Status switch
            {
                "Deleted" => TemporaryFileOutcome.DeletedVerified,
                "SkippedUnavailable" when result.NativeError == 32 => TemporaryFileOutcome.SkippedBusy,
                "SkippedChanged" => TemporaryFileOutcome.SkippedChanged,
                "SkippedProtected" => TemporaryFileOutcome.SkippedProtected,
                "BlockedScope" => TemporaryFileOutcome.BlockedScope,
                "BlockedStalePreview" => TemporaryFileOutcome.BlockedStale,
                "Cancelled" => TemporaryFileOutcome.Cancelled,
                _ => TemporaryFileOutcome.Unverified,
            };
            return Task.FromResult(new TemporaryFileResult(candidateId, outcome,
                outcome == TemporaryFileOutcome.DeletedVerified ? checked((long)result.LogicalBytes) :
                outcome == TemporaryFileOutcome.Unverified ? null : 0, result.NativeError));
        }
    }
}
