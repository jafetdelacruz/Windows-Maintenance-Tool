using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using WinMaint.Infrastructure.Win;

namespace WinMaint.CleanupLab;

internal static partial class Program
{
    private const string Mount = @"C:\WinMaintPhase8Cleanup";
    private const string Marker = @"C:\WinMaint-Phase8-Cleanup.marker";
    private const string Reports = @"C:\WinMaintPhase8Results";
    private const string Output = @"C:\WinMaintLabOutput";
    private static bool batchMode;
    private static bool writeSessionMarker = true;
    private static readonly string BatchId = Guid.NewGuid().ToString("N");
    private const string Approval = "DELETE LAB FILES";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private sealed record Snapshot(string Path, NativeFiles.FileIdentity Identity, long CapturedAt);
    private sealed record Outcome(string File, string Status, ulong LogicalBytes, int? NativeError = null);
    private sealed record Check(string Name, bool Passed);

    private static int Main(string[] args)
    {
        // Only a fixed synthetic batch is allowed; no roots or host override switches.
        bool integrationMode = args.Length == 1 && args[0] == "--integration-batch";
        batchMode = integrationMode || (args.Length == 1 && args[0] == "--synthetic-batch");
        if ((args.Length != 0 && !batchMode) || !OperatingSystem.IsWindows() ||
            !Environment.UserName.Equals("WDAGUtilityAccount", StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFullPath(AppContext.BaseDirectory).Equals(Mount + "\\", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("BLOCKED: this executable runs only in the supplied Windows Sandbox lab.");
            return 3;
        }
        if (File.Exists(Marker))
        {
            Console.WriteLine("RECREATION CHECK FAILED. Close Sandbox completely and reopen the profile.");
            return 4;
        }
        Console.WriteLine("RECREATION CHECK PASSED: no marker from a previous lab session.");
        try
        {
            if (!batchMode) return Run();
            if (!Directory.Exists(Output))
                throw new InvalidOperationException("Missing isolated lab output mapping.");
            if (integrationMode) return RunIntegrationAsync().GetAwaiter().GetResult();
            Console.WriteLine("AUTOMATED SYNTHETIC LAB: cancellation first; deletion only if it passes.");
            writeSessionMarker = false;
            int cancelled = Run(string.Empty);
            writeSessionMarker = true;
            if (cancelled != 0) return cancelled;
            int result = Run(Approval);
            if (result == 0)
                Console.WriteLine("AUTOMATED LAB PASSED: cancellation + 15/15. Reports exported. Production blocked.");
            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException or JsonException or OperationCanceledException)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"LAB FAILED: {ex.Message}. Production cleanup remains blocked.");
            Console.ResetColor();
            if (integrationMode) ExportIntegrationFailure(ex);
            return 1;
        }
    }

    private static int Run(string? scriptedAnswer = null)
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string temp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!Path.GetFullPath(temp).Equals(Path.Combine(profile, "AppData", "Local"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Redirected profile is not allowed.");
        string root = Path.Combine(temp, "Temp", "WinMaintPhase8CleanupLab", Guid.NewGuid().ToString("N"));
        // Pin all ancestors without share-delete; reject links using the opened objects.
        var pins = new List<SafeFileHandle>();
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
            if (!RootRenameBlocked(root))
                throw new InvalidOperationException("ROOT GUARD FAILED before file creation. No files deleted.");
            Console.WriteLine("ROOT GUARD PASSED: rename blocked before any deletion.");
            string old = Make(root, "old.txt", true);
            string busy = Make(root, "busy.txt", true);
            string changed = Make(root, "changed.txt", true);
            string recent = Make(root, "recent.txt", false);
            string readOnly = Make(root, "readonly.txt", true);
            File.SetAttributes(readOnly, FileAttributes.ReadOnly);
            string outside = Path.Combine(Path.GetDirectoryName(root)!, "sentinel-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(outside, "OUTSIDE SENTINEL");
            string link = Path.Combine(root, "link.txt");
            File.CreateSymbolicLink(link, outside);
            string[] paths = [old, busy, changed, recent, readOnly, link];
            Snapshot[] plan = paths.Select(Capture).ToArray();
            bool preflight = plan.Length == 6 && plan.All(s => SamePath(s.Path, root)) &&
                NativeFiles.Eligible(plan[0].Identity, DateTime.UtcNow.AddDays(-7));
            if (!preflight) throw new InvalidOperationException("Fixture preflight failed.");
            Console.WriteLine("LAB PREFLIGHT PASSED. Six fictitious files; no production folders selected.");
            Console.WriteLine("Deletion is irreversible inside this disposable VM. No directories are deleted.");
            if (!batchMode)
            {
                Console.WriteLine("FIRST TEST: press ENTER to cancel. Do not type the deletion token yet.");
                Console.WriteLine($"Later, after cancellation acceptance: type exactly {Approval} to run deletion tests.");
                Console.WriteLine("Preview expires after five minutes; reopen a fresh Sandbox if you wait longer.");
            }
            string? answer = batchMode ? scriptedAnswer : Console.ReadLine();
            var outcomes = new List<Outcome>();
            var checks = new List<Check>();
            using var stop = new CancellationTokenSource();
            ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; stop.Cancel(); };
            Console.CancelKeyPress += cancel;
            try
            {
                if (!string.Equals(answer, Approval, StringComparison.Ordinal))
                {
                    checks.Add(new("Exact confirmation absent: nothing deleted", paths.All(File.Exists) && File.ReadAllText(outside) == "OUTSIDE SENTINEL"));
                    outcomes.AddRange(plan.Select(s => new Outcome(Path.GetFileName(s.Path), "CancelledBeforeExecution", 0)));
                    Save(root, "Cancelled", outcomes, checks);
                    return Finish(checks, "CANCELLATION GATE PASSED: NO FILES DELETED.");
                }
                // Deliberate changes AFTER snapshot and BEFORE execution.
                File.AppendAllText(changed, "CHANGED AFTER PREVIEW");
                using (var held = new FileStream(busy, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    foreach (var snapshot in plan)
                        outcomes.Add(DeleteOne(snapshot, root, stop.Token));
                }
                checks.Add(new("Eligible file removed", !File.Exists(old) && outcomes[0].Status == "Deleted"));
                checks.Add(new("Busy file preserved", File.Exists(busy) && outcomes[1].Status == "SkippedUnavailable" && outcomes[1].NativeError == 32));
                checks.Add(new("Changed file preserved", File.Exists(changed) && outcomes[2].Status == "SkippedChanged"));
                checks.Add(new("Recent file preserved", File.Exists(recent) && outcomes[3].Status == "SkippedProtected"));
                checks.Add(new("ReadOnly file preserved", File.Exists(readOnly) && outcomes[4].Status == "SkippedProtected"));
                checks.Add(new("Link and outside target preserved", File.Exists(link) && File.ReadAllText(outside) == "OUTSIDE SENTINEL" && outcomes[5].Status == "SkippedProtected"));
                if (stop.IsCancellationRequested)
                {
                    Save(root, "Interrupted", outcomes, checks);
                    Console.WriteLine("INTERRUPTED: completed results preserved; no further deletion tests run.");
                    return 2;
                }
                string first = Make(root, "partial-first.txt", true);
                string second = Make(root, "partial-second.txt", true);
                Snapshot firstSnapshot = Capture(first), secondSnapshot = Capture(second);
                using var partialStop = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                var firstResult = DeleteOne(firstSnapshot, root, partialStop.Token);
                partialStop.Cancel();
                var secondResult = DeleteOne(secondSnapshot, root, partialStop.Token);
                outcomes.Add(firstResult);
                outcomes.Add(secondResult);
                checks.Add(new("Mid-batch cancellation reports completed deletion and preserves next file",
                    firstResult.Status == "Deleted" && !File.Exists(first) && secondResult.Status == "Cancelled" && File.Exists(second)));
                checks.Add(new("Directories preserved", Directory.Exists(root)));
                if (stop.IsCancellationRequested)
                {
                    Save(root, "Interrupted", outcomes, checks);
                    Console.WriteLine("INTERRUPTED: no hardening tests run.");
                    return 2;
                }
                RunHardening(root, outside, outcomes, checks, stop.Token);
                Save(root, stop.IsCancellationRequested ? "Interrupted" : "FinishedWithSkips", outcomes, checks);
                if (stop.IsCancellationRequested) return 2;
                return Finish(checks, "HARDENING LAB GATE PASSED: 15/15. PRODUCTION CLEANUP BLOCKED.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
            {
                checks.Add(new("Unexpected execution failure: " + ex.GetType().Name, false));
                Save(root, "FailedWithPartialResults", outcomes, checks);
                throw;
            }
            finally { Console.CancelKeyPress -= cancel; }
        }
        finally { foreach (var pin in Enumerable.Reverse(pins)) pin.Dispose(); }
    }

    private static void Pin(string path, List<SafeFileHandle> pins)
    {
        var handle = NativeFiles.Open(path, false, true);
        pins.Add(handle);
        var info = NativeFiles.Inspect(handle);
        if ((info.Attributes & (uint)FileAttributes.ReparsePoint) != 0 ||
            (info.Attributes & (uint)FileAttributes.Directory) == 0 ||
            !NativeFiles.FinalPath(handle).TrimEnd('\\').Equals(path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unsafe or substituted ancestor.");
    }

    private static bool SamePath(string path, string root) =>
        string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), root, StringComparison.OrdinalIgnoreCase);

    private static Snapshot Capture(string path)
    {
        using var handle = NativeFiles.Open(path, false);
        return new(path, NativeFiles.Inspect(handle), Stopwatch.GetTimestamp());
    }

    private static Outcome DeleteOne(Snapshot snapshot, string root, CancellationToken token)
    {
        var result = WindowsTemporaryFileDeletion.Execute(snapshot.Path, snapshot.Identity,
            snapshot.CapturedAt, root, token, executionEnabled: true);
        return new(Path.GetFileName(snapshot.Path), result.Status, result.LogicalBytes, result.NativeError);
    }

    private static void RunHardening(string root, string outside, List<Outcome> outcomes,
        List<Check> checks, CancellationToken token)
    {
        // All moves below are flat files inside this exact, owned GUID fixture.
        string replacement = Make(root, "replacement.txt", true);
        Snapshot replacedSnapshot = Capture(replacement);
        string retained = Path.Combine(root, "replacement-retained.txt");
        File.Move(replacement, retained);
        Make(root, "replacement.txt", true);
        File.SetCreationTimeUtc(replacement, DateTime.FromFileTimeUtc(replacedSnapshot.Identity.Creation));
        File.SetLastWriteTimeUtc(replacement, DateTime.FromFileTimeUtc(replacedSnapshot.Identity.Write));
        Outcome replaced = DeleteOne(replacedSnapshot, root, token);
        outcomes.Add(replaced);
        checks.Add(new("Same size and timestamps but different identity preserved",
            replaced.Status == "SkippedChanged" && File.Exists(replacement) && File.Exists(retained)));

        string substituted = Make(root, "substituted-link.txt", true);
        Snapshot linkSnapshot = Capture(substituted);
        string retainedLink = Path.Combine(root, "link-original-retained.txt");
        File.Move(substituted, retainedLink);
        File.CreateSymbolicLink(substituted, outside);
        Outcome linked = DeleteOne(linkSnapshot, root, token);
        outcomes.Add(linked);
        checks.Add(new("File replaced by symlink preserved with outside target",
            linked.Status == "SkippedChanged" && File.Exists(substituted) && File.Exists(retainedLink) &&
            File.ReadAllText(outside) == "OUTSIDE SENTINEL"));

        string multiple = Make(root, "hardlink-before.txt", true);
        string alias = Path.Combine(root, "hardlink-before-alias.txt");
        NativeFiles.AddHardLink(alias, multiple);
        Outcome before = DeleteOne(Capture(multiple), root, token);
        outcomes.Add(before);
        checks.Add(new("Hardlink present at preview excluded", before.Status == "SkippedProtected" &&
            File.Exists(multiple) && File.Exists(alias)));

        string after = Make(root, "hardlink-after.txt", true);
        Snapshot singleSnapshot = Capture(after);
        string afterAlias = Path.Combine(root, "hardlink-after-alias.txt");
        NativeFiles.AddHardLink(afterAlias, after);
        Outcome added = DeleteOne(singleSnapshot, root, token);
        outcomes.Add(added);
        checks.Add(new("Hardlink added after preview preserved", added.Status == "SkippedChanged" &&
            File.Exists(after) && File.Exists(afterAlias)));

        string stale = Make(root, "stale-preview.txt", true);
        Snapshot staleSnapshot = Capture(stale) with
        {
            CapturedAt = Stopwatch.GetTimestamp() - checked(Stopwatch.Frequency * 360L),
        };
        Outcome expired = DeleteOne(staleSnapshot, root, token);
        outcomes.Add(expired);
        checks.Add(new("Expired preview blocked even for unchanged eligible file",
            expired.Status == "BlockedStalePreview" && File.Exists(stale)));

        bool renameBlocked = RootRenameBlocked(root);
        checks.Add(new("Pinned root cannot be renamed during execution", renameBlocked));
        if (!renameBlocked) throw new InvalidOperationException("Root pinning failed; stop immediately.");

        // Scope guard must reject this before opening or changing the outside object.
        Outcome foreign = DeleteOne(singleSnapshot with { Path = outside }, root, token);
        outcomes.Add(foreign);
        checks.Add(new("Foreign parent blocked before opening file", foreign.Status == "BlockedScope" &&
            File.ReadAllText(outside) == "OUTSIDE SENTINEL"));
    }

    private static bool RootRenameBlocked(string root)
    {
        string movedRoot = root + "-rename-attempt";
        if (!string.Equals(Path.GetDirectoryName(root), Path.GetDirectoryName(movedRoot), StringComparison.OrdinalIgnoreCase) ||
            Directory.Exists(movedRoot)) throw new InvalidOperationException("Unsafe rename fixture target.");
        bool renameBlocked = false;
        try { Directory.Move(root, movedRoot); }
        catch (IOException) { renameBlocked = true; }
        return renameBlocked && Directory.Exists(root) && !Directory.Exists(movedRoot);
    }

    private static string Make(string root, string name, bool aged)
    {
        string path = Path.Combine(root, name);
        File.WriteAllText(path, "SYNTHETIC LAB FILE");
        if (aged)
        {
            File.SetCreationTimeUtc(path, DateTime.UtcNow.AddDays(-10));
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-10));
        }
        return path;
    }

    private static void Save(string root, string state, List<Outcome> outcomes, List<Check> checks)
    {
        // Keep evidence outside the directory intentionally subjected to rename attempts.
        foreach (var outcome in outcomes) Console.WriteLine($"{outcome.File}: {outcome.Status}");
        foreach (var check in checks.Where(c => !c.Passed)) Console.WriteLine($"FAIL: {check.Name}");
        string report = Path.Combine(Reports, Path.GetFileName(root) + "-" + Guid.NewGuid().ToString("N") + ".json");
        using (var reportStream = new FileStream(report, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(reportStream, new
            {
                SchemaVersion = "lab-1.0",
                LabVersion = "0.8.0",
                AutomatedSyntheticBatch = batchMode,
                BatchId = batchMode ? BatchId : null,
                State = state,
                ProductionEnabled = false,
                DeletedFiles = outcomes.Count(o => o.Status == "Deleted"),
                DeletedLogicalBytes = outcomes.Where(o => o.Status == "Deleted").Sum(o => (long)o.LogicalBytes),
                PhysicalSpaceFreed = "Not measured",
                Outcomes = outcomes,
                Checks = checks,
            }, JsonOptions);
        }
        if (batchMode)
            File.Copy(report, Path.Combine(Output, Path.GetFileName(report)), overwrite: false);
        if (writeSessionMarker)
        {
            using var marker = new FileStream(Marker, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        }
        Console.WriteLine($"Report: {report}");
    }

    private static int Finish(List<Check> checks, string success)
    {
        bool passed = checks.All(c => c.Passed);
        foreach (var check in checks) Console.WriteLine($"{(check.Passed ? "PASS" : "FAIL")}: {check.Name}");
        Console.ForegroundColor = passed ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine(passed ? success : "LAB GATE FAILED. PRODUCTION CLEANUP BLOCKED.");
        Console.ResetColor();
        return passed ? 0 : 1;
    }
}
