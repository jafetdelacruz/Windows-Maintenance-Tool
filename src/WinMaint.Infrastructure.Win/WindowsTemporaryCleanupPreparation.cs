using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32.SafeHandles;
using WinMaint.Application;
using WinMaint.Domain;

namespace WinMaint.Infrastructure.Win;

internal sealed record OwnedCleanupLabOptions(Action? AncestorsPinned = null, TimeSpan? PreviewAge = null);

// Public composition has no root argument and can NEVER enable deletion in this delivery.
public sealed class WindowsTemporaryCleanupPreparation : IPreparedTemporaryCleanupRunner
{
    private sealed record Ancestor(string Path, WindowsTemporaryNative.FileIdentity Identity);
    private sealed record Candidate(string Path, WindowsTemporaryNative.FileIdentity Identity,
        long CapturedAt, Ancestor[] Ancestors);
    private readonly Dictionary<string, Candidate> candidates = new(StringComparer.Ordinal);
    private readonly string root;
    private readonly bool executionEnabled;
    private OwnedCleanupLabOptions? labOptions;

    public TemporaryInventory Inventory { get; private set; }
    public IReadOnlyList<string> CandidateIds => Array.AsReadOnly(candidates.Keys.ToArray());
    public bool CanExecute => executionEnabled && Inventory.Coverage == EvidenceCoverage.Complete;

    private WindowsTemporaryCleanupPreparation(string root, bool executionEnabled)
    {
        this.root = root;
        this.executionEnabled = executionEnabled;
        Inventory = new(Guid.NewGuid(), DateTimeOffset.UtcNow, EvidenceCoverage.Absent, null, null, null, null, null);
    }

    public static Task<WindowsTemporaryCleanupPreparation> CreateForCurrentUserAsync(CancellationToken token = default)
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return PrepareAsync(profile, local, Path.Combine(local, "Temp"), false, token);
    }

    internal static async Task<WindowsTemporaryCleanupPreparation> CreateForOwnedLabAsync(string profile, CancellationToken token,
        OwnedCleanupLabOptions? options = null)
    {
        string labBase = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Temp", "WinMaintPhase8CleanupLab");
        string relative = Path.GetRelativePath(labBase, Path.GetFullPath(profile));
        string[] parts = relative.Split(Path.DirectorySeparatorChar);
        if (!Environment.UserName.Equals("WDAGUtilityAccount", StringComparison.OrdinalIgnoreCase) ||
            !AppContext.BaseDirectory.Equals(@"C:\WinMaintPhase8Cleanup\", StringComparison.OrdinalIgnoreCase) ||
            parts.Length != 2 || !Guid.TryParseExact(parts[0], "N", out _) || parts[1] != "synthetic-profile")
            throw new InvalidOperationException("Mutable preparation is restricted to an owned Sandbox fixture.");
        string local = Path.Combine(profile, "AppData", "Local");
        if (options?.PreviewAge is { } age && (age < TimeSpan.Zero || age > TimeSpan.FromHours(1)))
            throw new ArgumentOutOfRangeException(nameof(options));
        var prepared = await PrepareAsync(profile, local, Path.Combine(local, "Temp"), true, token).ConfigureAwait(false);
        prepared.labOptions = options;
        if (options?.PreviewAge is { } previewAge)
            foreach (var id in prepared.candidates.Keys.ToArray())
                prepared.candidates[id] = prepared.candidates[id] with
                { CapturedAt = prepared.candidates[id].CapturedAt - checked((long)(previewAge.TotalSeconds * Stopwatch.Frequency)) };
        return prepared;
    }

    internal static bool IsExpectedRoot(string profile, string local, string temp)
    {
        if (string.IsNullOrWhiteSpace(profile) || string.IsNullOrWhiteSpace(local) || string.IsNullOrWhiteSpace(temp)) return false;
        try
        {
            static string Full(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            string p = Full(profile), l = Full(local), t = Full(temp);
            return Path.IsPathFullyQualified(profile) && Path.IsPathFullyQualified(local) && Path.IsPathFullyQualified(temp) &&
                p.StartsWith(@"C:\", StringComparison.OrdinalIgnoreCase) && p.Length > 3 &&
                l.Equals(Path.Combine(p, "AppData", "Local"), StringComparison.OrdinalIgnoreCase) &&
                t.Equals(Path.Combine(l, "Temp"), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    private static async Task<WindowsTemporaryCleanupPreparation> PrepareAsync(string profile, string local,
        string temp, bool executionEnabled, CancellationToken token)
    {
        await Task.Yield();
        token.ThrowIfCancellationRequested();
        if (!IsExpectedRoot(profile, local, temp)) return new(temp, false);
        var prepared = new WindowsTemporaryCleanupPreparation(Path.GetFullPath(temp).TrimEnd(Path.DirectorySeparatorChar), executionEnabled);
        await prepared.CollectAsync(token).ConfigureAwait(false);
        return prepared;
    }

    private async Task CollectAsync(CancellationToken token)
    {
        await Task.Yield();
        long recent = 0, protectedItems = 0, unavailable = 0, logicalBytes = 0;
        int visited = 0;
        bool limited = false;
        var clock = Stopwatch.StartNew();
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((root, 0));
        while (pending.TryPop(out var folder))
        {
            token.ThrowIfCancellationRequested();
            if (clock.Elapsed >= TimeSpan.FromSeconds(15) || visited >= 20_000) { limited = true; break; }
            try
            {
                using var pins = Acquire(folder.Path, null);
                foreach (string entry in Directory.EnumerateFileSystemEntries(folder.Path))
                {
                    token.ThrowIfCancellationRequested();
                    if (++visited > 20_000 || clock.Elapsed >= TimeSpan.FromSeconds(15)) { limited = true; break; }
                    if (!entry.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Traversal outside the allowed root.");
                    try
                    {
                        var attributes = File.GetAttributes(entry);
                        if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.System | FileAttributes.Offline)) != 0)
                        { protectedItems++; continue; }
                        if ((attributes & FileAttributes.Directory) != 0)
                        {
                            if (WindowsTemporaryInventoryProvider.IsProtectedFolder(Path.GetFileName(entry))) protectedItems++;
                            else if (folder.Depth >= 32) { limited = true; protectedItems++; }
                            else pending.Push((entry, folder.Depth + 1));
                            continue;
                        }
                        using var handle = WindowsTemporaryNative.Open(entry, false);
                        var identity = WindowsTemporaryNative.Inspect(handle);
                        if (identity.Creation <= 0 || identity.Write <= 0)
                            throw new InvalidOperationException("Missing file timestamps.");
                        if (!WindowsTemporaryNative.FinalPath(handle).Equals(entry, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("Resolved file path differs from the allowed path.");
                        if ((identity.Attributes & (uint)(FileAttributes.ReparsePoint | FileAttributes.System |
                            FileAttributes.Offline | FileAttributes.ReadOnly | FileAttributes.Directory)) != 0 || identity.Links != 1)
                        { protectedItems++; continue; }
                        if (!WindowsTemporaryNative.Eligible(identity, DateTime.UtcNow - WindowsTemporaryInventoryProvider.MinimumAge))
                        { recent++; continue; }
                        logicalBytes = checked(logicalBytes + (long)identity.Size);
                        candidates.Add(Guid.NewGuid().ToString("N"), new(entry, identity, Stopwatch.GetTimestamp(), pins.Ancestors));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException or OverflowException or ArgumentOutOfRangeException)
                    { unavailable++; }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
            { unavailable++; }
            if (limited) break;
        }
        Inventory = new(Inventory.Id, Inventory.CapturedAt, limited || unavailable > 0 ? EvidenceCoverage.Partial : EvidenceCoverage.Complete,
            candidates.Count, logicalBytes, recent, protectedItems, unavailable);
    }

    public Task<TemporaryFileResult> RunAsync(string candidateId, CancellationToken cancellationToken)
    {
        if (!CanExecute || !candidates.TryGetValue(candidateId, out var candidate))
            return Task.FromResult(new TemporaryFileResult(candidateId, TemporaryFileOutcome.BlockedScope, 0));
        if (cancellationToken.IsCancellationRequested)
            return Task.FromResult(new TemporaryFileResult(candidateId, TemporaryFileOutcome.Cancelled, 0));
        DirectoryPins pins;
        try
        {
            pins = Acquire(Path.GetDirectoryName(candidate.Path)!, candidate.Ancestors);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            return Task.FromResult(new TemporaryFileResult(candidateId, TemporaryFileOutcome.BlockedScope, 0,
                ex is Win32Exception win32 ? win32.NativeErrorCode : null));
        }
        using (pins)
        {
            labOptions?.AncestorsPinned?.Invoke();
            // Once execution starts, unexpected errors must reach the coordinator as uncertain results.
            var result = WindowsTemporaryFileDeletion.Execute(candidate.Path, candidate.Identity, candidate.CapturedAt,
                Path.GetDirectoryName(candidate.Path)!, cancellationToken, executionEnabled: true);
            var outcome = result.Status switch
            {
                "Deleted" => TemporaryFileOutcome.DeletedVerified,
                "SkippedUnavailable" when result.NativeError == 32 => TemporaryFileOutcome.SkippedBusy,
                "SkippedChanged" => TemporaryFileOutcome.SkippedChanged,
                "SkippedProtected" => TemporaryFileOutcome.SkippedProtected,
                "BlockedStalePreview" => TemporaryFileOutcome.BlockedStale,
                "BlockedScope" or "BlockedIdentityOrPath" => TemporaryFileOutcome.BlockedScope,
                "Cancelled" => TemporaryFileOutcome.Cancelled,
                _ => TemporaryFileOutcome.Unverified,
            };
            return Task.FromResult(new TemporaryFileResult(candidateId, outcome,
                outcome == TemporaryFileOutcome.DeletedVerified ? checked((long)result.LogicalBytes) :
                outcome == TemporaryFileOutcome.Unverified ? null : 0, result.NativeError));
        }
    }

    private DirectoryPins Acquire(string directory, Ancestor[]? expected)
    {
        if (!directory.Equals(root, StringComparison.OrdinalIgnoreCase) &&
            !directory.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Directory outside the prepared scope.");
        var pins = new DirectoryPins();
        try
        {
            string current = Path.GetPathRoot(directory)!;
            var paths = new List<string> { current };
            foreach (string part in directory[current.Length..].Split(Path.DirectorySeparatorChar))
            {
                current = Path.Combine(current, part);
                paths.Add(current);
            }
            foreach (string path in paths)
            {
                var handle = WindowsTemporaryNative.Open(path, false, true);
                pins.Handles.Add(handle);
                var id = WindowsTemporaryNative.Inspect(handle);
                if ((id.Attributes & (uint)FileAttributes.ReparsePoint) != 0 || (id.Attributes & (uint)FileAttributes.Directory) == 0 ||
                    !WindowsTemporaryNative.FinalPath(handle).TrimEnd('\\').Equals(path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Unsafe directory ancestor.");
                if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
                    (id.Attributes & (uint)(FileAttributes.System | FileAttributes.Offline)) != 0)
                    throw new InvalidOperationException("Directory became protected.");
                pins.Items.Add(new(path, id));
            }
            if (expected is not null && (expected.Length != pins.Items.Count || expected.Where((item, i) =>
                !SameObject(item.Identity, pins.Items[i].Identity) ||
                !item.Path.Equals(pins.Items[i].Path, StringComparison.OrdinalIgnoreCase)).Any()))
                throw new InvalidOperationException("Directory identity changed since preview.");
            return pins;
        }
        catch { pins.Dispose(); throw; }
    }

    private static bool SameObject(WindowsTemporaryNative.FileIdentity a, WindowsTemporaryNative.FileIdentity b) =>
        a.Volume == b.Volume && a.IndexHigh == b.IndexHigh && a.IndexLow == b.IndexLow && a.Creation == b.Creation;

    private sealed class DirectoryPins : IDisposable
    {
        public List<SafeFileHandle> Handles { get; } = [];
        public List<Ancestor> Items { get; } = [];
        public Ancestor[] Ancestors => Items.ToArray();
        public void Dispose() { foreach (var handle in Enumerable.Reverse(Handles)) handle.Dispose(); }
    }
}
