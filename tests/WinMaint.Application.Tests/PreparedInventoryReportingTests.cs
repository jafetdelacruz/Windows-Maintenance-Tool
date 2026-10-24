using WinMaint.Domain;
using WinMaint.Reporting;

namespace WinMaint.Application.Tests;

public sealed class PreparedInventoryReportingTests
{
    [Fact]
    public void TypedInventoryKeepsOriginalIdentityTimestampAndExactLogicalBytes()
    {
        var inventory = new TemporaryInventory(Guid.NewGuid(), DateTimeOffset.UnixEpoch,
            EvidenceCoverage.Complete, 1, 9_007_199_254_740_993, 2, 3, 0);
        var journal = new ReportJournal("unused-no-write");
        journal.AddPreparedTemporaryInventory(inventory);
        var parsed = ReportRenderer.Parse(ReportRenderer.ToJson(journal.CreateReport("Partial")));
        var record = Assert.Single(parsed.TemporaryCleanup);
        Assert.Equal(inventory, record.Inventory);
        Assert.Equal("InventoryOnly", record.Status);
        Assert.Empty(record.Files);
        Assert.Equal(0, record.DeletedFiles);
    }

    [Fact]
    public void MissingRootIsUnavailableNotAnEmptyHealthyInventory()
    {
        var inventory = new TemporaryInventory(Guid.NewGuid(), DateTimeOffset.UtcNow,
            EvidenceCoverage.Absent, null, null, null, null, null);
        var journal = new ReportJournal("unused-no-write");
        journal.AddPreparedTemporaryInventory(inventory);
        var parsed = ReportRenderer.Parse(ReportRenderer.ToJson(journal.CreateReport("Partial")));
        Assert.Equal(inventory, Assert.Single(parsed.TemporaryCleanup).Inventory);
        Assert.Contains("desconocido", ReportRenderer.ToText(parsed));
    }

    [Fact]
    public void PartialInventoryIsNotPromotedToComplete()
    {
        var inventory = new TemporaryInventory(Guid.NewGuid(), DateTimeOffset.UtcNow,
            EvidenceCoverage.Partial, 1, 18, 1, 0, 1);
        var journal = new ReportJournal("unused-no-write");
        journal.AddPreparedTemporaryInventory(inventory);
        Assert.Equal(inventory, Assert.Single(journal.CreateReport("Partial").TemporaryCleanup).Inventory);
    }
}
