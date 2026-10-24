using WinMaint.Application;
using WinMaint.Domain;

namespace WinMaint.Infrastructure.Win;

public sealed class TemporaryFileMaintenanceRunnerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task InventoryCountsOnlyFilesOlderThanMinimumAge()
    {
        using var directory = new TemporaryTestDirectory();
        directory.CreateFile("old.bin", 7, Now.AddDays(-2).UtcDateTime);
        directory.CreateFile("recent.bin", 11, Now.AddHours(-2).UtcDateTime);
        var runner = directory.CreateRunner(() => Now);

        var result = await runner.RunAsync(Item("temp.inventory"), CancellationToken.None);

        Assert.Equal(MaintenanceOutcome.Succeeded, result.Outcome);
        Assert.Equal(1L, result.AffectedFiles);
        Assert.Equal(7L, result.AffectedBytes);
        Assert.False(result.KnownTemporaryArtifactsRemain);
        Assert.True(File.Exists(Path.Combine(directory.TempRoot, "old.bin")));
    }

    [Fact]
    public async Task CleanupRequiresCurrentInventory()
    {
        using var directory = new TemporaryTestDirectory();
        var runner = directory.CreateRunner(() => Now);

        var result = await runner.RunAsync(Item("temp.cleanup"), CancellationToken.None);

        Assert.Equal(MaintenanceOutcome.Blocked, result.Outcome);
        Assert.Equal(0L, result.AffectedFiles);
    }

    [Fact]
    public async Task CleanupDeletesOnlyUnchangedInventoriedFiles()
    {
        using var directory = new TemporaryTestDirectory();
        var deletedPath = directory.CreateFile("delete.bin", 7, Now.AddDays(-2).UtcDateTime);
        var changedPath = directory.CreateFile("changed.bin", 5, Now.AddDays(-3).UtcDateTime);
        var runner = directory.CreateRunner(() => Now);
        await runner.RunAsync(Item("temp.inventory"), CancellationToken.None);
        await File.AppendAllTextAsync(changedPath, "changed");

        var result = await runner.RunAsync(Item("temp.cleanup"), CancellationToken.None);

        Assert.Equal(MaintenanceOutcome.Failed, result.Outcome);
        Assert.Equal(1L, result.AffectedFiles);
        Assert.Equal(7L, result.AffectedBytes);
        Assert.False(File.Exists(deletedPath));
        Assert.True(File.Exists(changedPath));
        Assert.False(result.KnownTemporaryArtifactsRemain);
    }

    [Fact]
    public async Task CancellationDoesNotLeaveAdapterArtifacts()
    {
        using var directory = new TemporaryTestDirectory();
        var path = directory.CreateFile("old.bin", 7, Now.AddDays(-2).UtcDateTime);
        var runner = directory.CreateRunner(() => Now);
        await runner.RunAsync(Item("temp.inventory"), CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await runner.RunAsync(Item("temp.cleanup"), cancellation.Token);

        Assert.Equal(MaintenanceOutcome.Cancelled, result.Outcome);
        Assert.True(File.Exists(path));
        Assert.False(result.KnownTemporaryArtifactsRemain);
    }

    [Fact]
    public async Task InventoryBlocksRootOutsideDeclaredProfile()
    {
        using var directory = new TemporaryTestDirectory();
        var unrelatedProfile = Path.Combine(directory.Root, "other-profile");
        Directory.CreateDirectory(unrelatedProfile);
        var runner = new TemporaryFileMaintenanceRunner(
            directory.TempRoot,
            unrelatedProfile,
            utcNow: () => Now);

        var result = await runner.RunAsync(Item("temp.inventory"), CancellationToken.None);

        Assert.Equal(MaintenanceOutcome.Blocked, result.Outcome);
        Assert.Null(result.AffectedFiles);
        Assert.Null(result.AffectedBytes);
    }

    private static MaintenancePlanItem Item(string actionId)
    {
        var action = MaintenanceCatalog.SafeActions.Single(item => item.Id == actionId);
        return new MaintenancePlanItem(
            action,
            action.Preconditions.Select(precondition => new MaintenancePreconditionResult(
                precondition.Id,
                MaintenancePreconditionStatus.Pass,
                "Fixture aprobada.")).ToArray());
    }

    private sealed class TemporaryTestDirectory : IDisposable
    {
        public TemporaryTestDirectory()
        {
            Root = Path.Combine(Path.GetTempPath(), "WinMaintTests", Guid.NewGuid().ToString("N"));
            ProfileRoot = Path.Combine(Root, "profile");
            TempRoot = Path.Combine(ProfileRoot, "AppData", "Local", "Temp");
            Directory.CreateDirectory(TempRoot);
        }

        public string Root { get; }
        public string ProfileRoot { get; }
        public string TempRoot { get; }

        public string CreateFile(string name, int length, DateTime lastWriteTimeUtc)
        {
            var path = Path.Combine(TempRoot, name);
            File.WriteAllBytes(path, new byte[length]);
            File.SetLastWriteTimeUtc(path, lastWriteTimeUtc);
            return path;
        }

        public TemporaryFileMaintenanceRunner CreateRunner(Func<DateTimeOffset> utcNow) =>
            new(TempRoot, ProfileRoot, utcNow: utcNow);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                // El directorio de fixture se limita a esta prueba y se intentará limpiar al cerrar el proceso.
            }
            catch (UnauthorizedAccessException)
            {
                // Un archivo todavía abierto no cambia la aserción funcional de la prueba.
            }
        }
    }
}
