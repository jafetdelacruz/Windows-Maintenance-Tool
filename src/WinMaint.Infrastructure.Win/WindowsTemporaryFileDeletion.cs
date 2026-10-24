using System.ComponentModel;
using System.Diagnostics;

namespace WinMaint.Infrastructure.Win;

internal sealed record WindowsTemporaryDeletionResult(string Status, ulong LogicalBytes, int? NativeError = null);

// Internal core, not a public path-based cleanup command. The caller must retain validated
// directory pins and own the candidate map for the entire operation. No production caller.
internal static class WindowsTemporaryFileDeletion
{
    internal static WindowsTemporaryDeletionResult Execute(string path,
        WindowsTemporaryNative.FileIdentity expected, long capturedAt,
        string pinnedParent, CancellationToken token, bool executionEnabled = false)
    {
        if (!executionEnabled) return new("BlockedScope", 0);
        if (token.IsCancellationRequested) return new("Cancelled", 0);
        if (Stopwatch.GetElapsedTime(capturedAt) > TimeSpan.FromMinutes(5)) return new("BlockedStalePreview", 0);
        if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), pinnedParent, StringComparison.OrdinalIgnoreCase))
            return new("BlockedScope", 0);
        if (!WindowsTemporaryNative.Eligible(expected, DateTime.UtcNow - WindowsTemporaryInventoryProvider.MinimumAge))
            return new("SkippedProtected", 0);
        try
        {
            using (var handle = WindowsTemporaryNative.Open(path, true))
            {
                var current = WindowsTemporaryNative.Inspect(handle);
                if (!current.Equals(expected)) return new("SkippedChanged", 0);
                if (!WindowsTemporaryNative.Eligible(current, DateTime.UtcNow - WindowsTemporaryInventoryProvider.MinimumAge) ||
                    !WindowsTemporaryNative.FinalPath(handle).Equals(path, StringComparison.OrdinalIgnoreCase))
                    return new("BlockedIdentityOrPath", 0);
                if (token.IsCancellationRequested) return new("Cancelled", 0);
                if (Stopwatch.GetElapsedTime(capturedAt) > TimeSpan.FromMinutes(5)) return new("BlockedStalePreview", 0);
                WindowsTemporaryNative.MarkDeleted(handle);
            }
            try
            {
                _ = File.GetAttributes(path);
                return new("DeletionNotVerified", 0);
            }
            catch (FileNotFoundException) { return new("Deleted", expected.Size); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Access denied is not proof of deletion; File.Exists would conceal it.
                return new("DeletionNotVerified", 0);
            }
        }
        catch (Win32Exception ex) { return new("SkippedUnavailable", 0, ex.NativeErrorCode); }
    }
}
