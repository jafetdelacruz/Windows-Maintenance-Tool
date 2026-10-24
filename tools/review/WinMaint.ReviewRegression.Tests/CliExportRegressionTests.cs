using Spectre.Console;
using WinMaint.Application;
using WinMaint.Cli;
using WinMaint.Reporting;

namespace WinMaint.ReviewRegression.Tests;

public sealed class CliExportRegressionTests
{
    [Theory]
    [InlineData(false, ".json", 4)]
    [InlineData(false, ".txt", 4)]
    [InlineData(true, ".json", 4)]
    [InlineData(true, ".txt", 4)]
    [InlineData(false, "none", 0)]
    [InlineData(true, "none", 0)]
    public async Task RequestedExportReturnsFailureForEitherWriteAndSuccessOnlyForCompletePair(
        bool reportMode, string failingExtension, int expected)
    {
        using var output = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(output),
        });
        var writer = new Writer(failingExtension);
        var shell = new WinMaintShell(console, true, new ReadOnlyDiagnosticCoordinator([]),
            new StabilityTestCoordinator([], new Store(), StabilityTestOptions.Standard), new Store(),
            null!, null!, new ReportJournal("unused-no-io", writer), null!);
        var exit = await shell.RunAsync(false, !reportMode, false, false, false, false, reportMode);
        Assert.Equal(expected, exit);
        if (expected == 4)
        {
            Assert.DoesNotContain("TXT:", output.ToString());
            Assert.Contains("memoria", output.ToString());
        }
        else Assert.True(ReportRenderer.IsMatchingExport(writer.Json!, writer.Text!));
    }

    private sealed class Writer(string failingExtension) : IReportFileWriter
    {
        public string? Json { get; private set; }
        public string? Text { get; private set; }
        public Task WriteAsync(string path, string content, CancellationToken cancellationToken)
        {
            if (Path.GetExtension(path) == failingExtension) throw new IOException("Synthetic failure");
            if (Path.GetExtension(path) == ".json") Json = content;
            else Text = content;
            return Task.CompletedTask;
        }
    }

    private sealed class Store : IStabilitySessionStore
    {
        public Task<InterruptedStabilitySession?> ReadInterruptedAsync(CancellationToken cancellationToken) =>
            Task.FromResult<InterruptedStabilitySession?>(null);
        public Task StartAsync(DateTimeOffset startedAt, CancellationToken cancellationToken) => throw new InvalidOperationException("No load allowed");
        public Task UpdateAsync(string completedProbeId, CancellationToken cancellationToken) => throw new InvalidOperationException("No load allowed");
        public Task CompleteAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("No load allowed");
    }
}
