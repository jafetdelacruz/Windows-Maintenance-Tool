using WinMaint.Infrastructure.Win;

namespace WinMaint.Windows.Tests;

public sealed class TemporaryRootPolicyTests
{
    [Theory]
    [InlineData(@"C:\Users\Fixture", @"C:\Users\Fixture\AppData\Local", @"C:\Users\Fixture\AppData\Local\Temp", true)]
    [InlineData(@"C:\Users\Fixture", @"C:\Users\Fixture\AppData\Local", @"c:\users\fixture\appdata\local\temp\", true)]
    [InlineData(@"C:\Users\Fixture", @"D:\Redirected", @"D:\Redirected\Temp", false)]
    [InlineData(@"C:\Users\Fixture", @"C:\Users\Other\AppData\Local", @"C:\Users\Other\AppData\Local\Temp", false)]
    [InlineData(@"C:\Users\Fixture", @"C:\Users\Fixture\AppData\Local", @"C:\Users\Fixture\Downloads", false)]
    [InlineData(@"C:\Users\Fixture", @"C:\Users\Fixture\AppData\Local", @"C:\Users\Fixture\AppData\Local\TempSibling", false)]
    [InlineData(@"C:\Users\Fixture", @"C:\Users\Fixture\AppData\Local", @"C:\Users\Fixture\AppData\Local\Temp\..\Other", false)]
    [InlineData(@"\\server\users\Fixture", @"\\server\users\Fixture\AppData\Local", @"\\server\users\Fixture\AppData\Local\Temp", false)]
    [InlineData(@"D:\Users\Fixture", @"D:\Users\Fixture\AppData\Local", @"D:\Users\Fixture\AppData\Local\Temp", false)]
    [InlineData("", "", "", false)]
    [InlineData("relative", @"C:\relative\AppData\Local", @"C:\relative\AppData\Local\Temp", false)]
    public void OnlyExactLocalUserTempIsAccepted(string profile, string local, string temp, bool expected)
    {
        Assert.Equal(expected, WindowsTemporaryCleanupPreparation.IsExpectedRoot(profile, local, temp));
    }

    [Fact]
    public async Task MutableLabPreparationIsRejectedOnHostBeforeTraversingFixture()
    {
        if (Environment.UserName.Equals("WDAGUtilityAccount", StringComparison.OrdinalIgnoreCase)) return;
        await Assert.ThrowsAsync<InvalidOperationException>(() => WindowsTemporaryCleanupPreparation.CreateForOwnedLabAsync(
            @"C:\never-created\synthetic-profile", CancellationToken.None));
    }

    [Fact]
    public async Task CancelledPreparationDoesNotAccessTheUserRoot()
    {
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            WindowsTemporaryCleanupPreparation.CreateForCurrentUserAsync(stop.Token));
    }
}
