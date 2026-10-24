using WinMaint.Application;
using WinMaint.Domain;

namespace WinMaint.Infrastructure.Win;

// No root or enable-execution parameter: the CLI cannot opt into lab composition.
public sealed class WindowsPreparedTemporaryInventoryProvider : ITemporaryInventoryProvider
{
    public async Task<TemporaryInventory> CollectAsync(CancellationToken cancellationToken = default)
    {
        var preparation = await WindowsTemporaryCleanupPreparation.CreateForCurrentUserAsync(cancellationToken)
            .ConfigureAwait(false);
        return preparation.Inventory;
    }
}
