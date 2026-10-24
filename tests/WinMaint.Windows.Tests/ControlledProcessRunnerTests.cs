using WinMaint.Infrastructure.Win;

namespace WinMaint.Windows.Tests;

public sealed class ControlledProcessRunnerTests
{
    [Fact]
    public async Task CancelledBeforeStartDoesNotAttemptExecutableLaunch()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ControlledProcessRunner.RunAsync(
            @"C:\nonexistent-winmaint-fixture.exe", [], TimeSpan.FromSeconds(1), cancellation.Token));
    }

    [Fact]
    public async Task RelativeExecutableCannotBeResolvedViaPath() =>
        await Assert.ThrowsAsync<ArgumentException>(() => ControlledProcessRunner.RunAsync(
            "powershell.exe", [], TimeSpan.FromSeconds(1), CancellationToken.None));
}
