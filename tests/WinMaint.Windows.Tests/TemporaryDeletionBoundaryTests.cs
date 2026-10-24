using System.Diagnostics;
using WinMaint.Infrastructure.Win;

namespace WinMaint.Windows.Tests;

public sealed class TemporaryDeletionBoundaryTests
{
    [Fact]
    public void NativeCoreIsDisabledBeforeAnyPathOrFileAccess()
    {
        var result = WindowsTemporaryFileDeletion.Execute("", default, 0, "", CancellationToken.None);
        Assert.Equal("BlockedScope", result.Status);
        Assert.Equal(0UL, result.LogicalBytes);
    }

    [Fact]
    public void CancellationPreventsAnyPathOrFileAccess()
    {
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        var result = WindowsTemporaryFileDeletion.Execute("", default, Stopwatch.GetTimestamp(), "", stop.Token, true);
        Assert.Equal("Cancelled", result.Status);
    }

    [Fact]
    public void ExpiredSnapshotPreventsAnyPathOrFileAccess()
    {
        long expired = Stopwatch.GetTimestamp() - checked(Stopwatch.Frequency * 360L);
        var result = WindowsTemporaryFileDeletion.Execute("", default, expired, "", CancellationToken.None, true);
        Assert.Equal("BlockedStalePreview", result.Status);
    }

    [Fact]
    public void ForeignParentIsRejectedWithoutOpeningAFile()
    {
        var result = WindowsTemporaryFileDeletion.Execute(@"C:\synthetic-only\file.txt", default,
            Stopwatch.GetTimestamp(), @"C:\different-synthetic-only", CancellationToken.None, true);
        Assert.Equal("BlockedScope", result.Status);
    }
}
