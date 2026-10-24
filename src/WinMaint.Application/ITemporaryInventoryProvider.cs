using WinMaint.Domain;

namespace WinMaint.Application;

// Presentation receives only aggregate metadata, never paths, candidate handles or a mutable runner.
public interface ITemporaryInventoryProvider
{
    Task<TemporaryInventory> CollectAsync(CancellationToken cancellationToken = default);
}
