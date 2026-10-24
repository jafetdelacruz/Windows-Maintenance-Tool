using WinMaint.Domain;

namespace WinMaint.Infrastructure.Win;

public sealed class TemporaryInventoryPreviewTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PreviewCountsOnlyOldFilesAndDoesNotChangeContents()
    {
        using var fixture = new InventoryFixture();
        var old = fixture.File("old.bin", 7, Now.AddDays(-8), Now.AddDays(-8));
        var recent = fixture.File("recent.bin", 11, Now.AddDays(-1), Now.AddDays(-1));
        var newlyCreated = fixture.File("new-with-old-timestamp.bin", 13, Now.AddHours(-1), Now.AddDays(-10));
        var section = await fixture.Provider().CollectAsync(CancellationToken.None);
        Assert.Equal(1, Number(section, "temp.preview.files.count"));
        Assert.Equal(7, Number(section, "temp.preview.bytes"));
        Assert.Equal(2, Number(section, "temp.preview.recent.count"));
        Assert.Equal(new byte[7], await System.IO.File.ReadAllBytesAsync(old));
        Assert.Equal(11, new FileInfo(recent).Length);
        Assert.Equal(13, new FileInfo(newlyCreated).Length);
    }

    [Fact]
    public async Task ProfileRootAndOtherFoldersCannotBeInventoried()
    {
        using var fixture = new InventoryFixture();
        foreach (var path in new[] { fixture.Profile, Path.Combine(fixture.Profile, "Downloads"), Path.GetDirectoryName(fixture.Profile)! })
        {
            var section = await new WindowsTemporaryInventoryProvider(fixture.Profile, path).CollectAsync(CancellationToken.None);
            Assert.Equal(EvidenceCoverage.Absent, section.Coverage);
            Assert.DoesNotContain(section.Data, datum => datum.MetricKey == "temp.preview.bytes");
        }
    }

    [Fact]
    public async Task ProtectedRuntimeFoldersAreNotTraversed()
    {
        using var fixture = new InventoryFixture();
        Directory.CreateDirectory(Path.Combine(fixture.Temp, ".net"));
        Directory.CreateDirectory(Path.Combine(fixture.Temp, "WinMaintTests"));
        fixture.File(Path.Combine(".net", "old.bin"), 20, Now.AddDays(-8), Now.AddDays(-8));
        fixture.File(Path.Combine("WinMaintTests", "old.bin"), 20, Now.AddDays(-8), Now.AddDays(-8));
        var section = await fixture.Provider().CollectAsync(CancellationToken.None);
        Assert.Equal(0, Number(section, "temp.preview.files.count"));
        Assert.Equal(2, Number(section, "temp.preview.protected.count"));
    }

    [Fact]
    public async Task ReadOnlyFilesAreExcludedWithoutChangingAttributes()
    {
        using var fixture = new InventoryFixture();
        var path = fixture.File("protected.bin", 9, Now.AddDays(-8), Now.AddDays(-8));
        System.IO.File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            var section = await fixture.Provider().CollectAsync(CancellationToken.None);
            Assert.Equal(0, Number(section, "temp.preview.files.count"));
            Assert.Equal(1, Number(section, "temp.preview.protected.count"));
            Assert.True((System.IO.File.GetAttributes(path) & FileAttributes.ReadOnly) != 0);
        }
        finally
        {
            System.IO.File.SetAttributes(path, FileAttributes.Normal);
        }
    }

    [Fact]
    public async Task TraversalLimitIsReportedAsPartialNotComplete()
    {
        using var fixture = new InventoryFixture();
        fixture.File("a.bin", 3, Now.AddDays(-8), Now.AddDays(-8));
        fixture.File("b.bin", 3, Now.AddDays(-8), Now.AddDays(-8));
        var section = await new WindowsTemporaryInventoryProvider(fixture.Profile, fixture.Temp, () => Now, maximumEntries: 1)
            .CollectAsync(CancellationToken.None);
        Assert.Equal(EvidenceCoverage.Partial, section.Coverage);
        Assert.Contains(section.Data, datum => datum.Name == "Estado del recorrido" && datum.Value == "Parcial");
    }

    [Fact]
    public async Task CancellationPreservesFixture()
    {
        using var fixture = new InventoryFixture();
        var path = fixture.File("a.bin", 3, Now.AddDays(-8), Now.AddDays(-8));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Provider().CollectAsync(cancellation.Token));
        Assert.True(System.IO.File.Exists(path));
    }

    private static double? Number(WinMaint.Application.DiagnosticSection section, string key) =>
        section.Data.Single(datum => datum.MetricKey == key).NumericValue;

    private sealed class InventoryFixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "WinMaintTests", Guid.NewGuid().ToString("N"));
        public InventoryFixture()
        {
            Profile = Path.Combine(root, "profile");
            Temp = Path.Combine(Profile, "AppData", "Local", "Temp");
            Directory.CreateDirectory(Temp);
        }

        public string Profile { get; }
        public string Temp { get; }
        public WindowsTemporaryInventoryProvider Provider() => new(Profile, Temp, () => Now);

        public string File(string name, int length, DateTimeOffset created, DateTimeOffset written)
        {
            var path = Path.Combine(Temp, name);
            System.IO.File.WriteAllBytes(path, new byte[length]);
            System.IO.File.SetCreationTimeUtc(path, created.UtcDateTime);
            System.IO.File.SetLastWriteTimeUtc(path, written.UtcDateTime);
            return path;
        }

        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
