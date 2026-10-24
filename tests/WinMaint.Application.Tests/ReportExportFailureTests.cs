using WinMaint.Reporting;

namespace WinMaint.Application.Tests;

public sealed class ReportExportFailureTests
{
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public async Task EitherWriteFailureIsNotSuccessfulExportAndRetryProducesMatchingPair(int failure, bool jsonSaved)
    {
        var writer = new MemoryWriter();
        var journal = new ReportJournal("unused-no-io", writer);
        Assert.True((await journal.TrySaveAsync("Partial")).Succeeded);
        var oldJson = writer.Files[journal.JsonPath];
        var oldText = writer.Files[journal.TextPath];
        writer.FailAt = writer.Calls + failure;
        var failed = await journal.TrySaveAsync("Completed");
        Assert.False(failed.Succeeded);
        Assert.Equal(4, failed.ExportExitCode);
        Assert.Equal(jsonSaved, failed.JsonSaved);
        Assert.False(failed.TextSaved);
        Assert.Equal(oldText, writer.Files[journal.TextPath]);
        if (jsonSaved)
        {
            Assert.NotEqual(oldJson, writer.Files[journal.JsonPath]);
            Assert.False(ReportRenderer.IsMatchingExport(writer.Files[journal.JsonPath], oldText));
        }
        else Assert.Equal(oldJson, writer.Files[journal.JsonPath]);
        writer.FailAt = 0;
        var retried = await journal.TrySaveAsync("Completed");
        Assert.True(retried.Succeeded);
        Assert.Equal(0, retried.ExportExitCode);
        Assert.True(ReportRenderer.IsMatchingExport(writer.Files[journal.JsonPath], writer.Files[journal.TextPath]));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ExistingSaveContractStillThrowsSoOldLabCallersCannotIgnoreFailure(int failure)
    {
        var journal = new ReportJournal("unused-no-io", new MemoryWriter { FailAt = failure });
        await Assert.ThrowsAsync<IOException>(() => journal.SaveAsync("Partial"));
    }

    private sealed class MemoryWriter : IReportFileWriter
    {
        public Dictionary<string, string> Files { get; } = [];
        public int Calls { get; private set; }
        public int FailAt { get; set; }
        public Task WriteAsync(string path, string content, CancellationToken cancellationToken)
        {
            Calls++;
            if (Calls == FailAt) throw new IOException("Synthetic write failure");
            Files[path] = content;
            return Task.CompletedTask;
        }
    }
}
