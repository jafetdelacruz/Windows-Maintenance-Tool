using WinMaint.Application;
using WinMaint.Domain;
using WinMaint.Infrastructure.Win;

namespace WinMaint.Windows.Tests;

public sealed class PreparedInventoryBoundaryTests
{
    [Fact]
    public void CliProviderExposesOnlyAnAggregateReadOnlyContract()
    {
        var provider = new WindowsPreparedTemporaryInventoryProvider();
        Assert.IsAssignableFrom<ITemporaryInventoryProvider>(provider);
        Assert.False(typeof(ITemporaryCleanupRunner).IsAssignableFrom(provider.GetType()));
        var method = Assert.Single(typeof(ITemporaryInventoryProvider).GetMethods());
        Assert.Equal(typeof(Task<TemporaryInventory>), method.ReturnType);
    }

    [Fact]
    public void PublicCompositionHasNoRootOrExecutionFlag()
    {
        var constructor = Assert.Single(typeof(WindowsPreparedTemporaryInventoryProvider).GetConstructors());
        Assert.Empty(constructor.GetParameters());
    }

    [Fact]
    public async Task CancelledCliProviderDoesNotPublishAnInventory()
    {
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new WindowsPreparedTemporaryInventoryProvider().CollectAsync(stop.Token));
    }
}
