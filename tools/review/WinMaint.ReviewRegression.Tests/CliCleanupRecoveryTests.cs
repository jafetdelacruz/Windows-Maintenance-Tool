using System.Globalization;
using Spectre.Console;
using WinMaint.Application;
using WinMaint.Cli;
using WinMaint.Domain;
using WinMaint.Reporting;

namespace WinMaint.ReviewRegression.Tests;

public sealed class CliCleanupRecoveryTests
{
    [Theory]
    [InlineData("interrupted")]
    [InlineData("corrupt")]
    [InlineData("unreadable")]
    [InlineData("closed")]
    public async Task StartupExportsRecoveryWithoutResumingOrClaimingNoChanges(string scenario)
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(output),
        });
        var inventory = new TemporaryInventory(Guid.NewGuid(), DateTimeOffset.UtcNow,
            EvidenceCoverage.Complete, 3, 54, 0, 0, 0);
        var recovery = new TemporaryCleanupRecovery(Guid.NewGuid(), inventory, scenario == "closed",
            scenario == "corrupt", "Completed",
            [new("first", TemporaryFileOutcome.DeletedVerified, 18),
                new("second", TemporaryFileOutcome.Unverified, null)], ["third"]);
        var reader = new Reader(scenario, recovery);
        var writer = new Writer();
        var store = new NoWorkStore();
        var shell = new WinMaintShell(console, true, new ReadOnlyDiagnosticCoordinator([]),
            new StabilityTestCoordinator([], store, StabilityTestOptions.Standard), store,
            null!, null!, new ReportJournal("unused-no-io", writer), null!, reader);
        Assert.Equal(0, await shell.RunAsync(false, false, false, false, false, false, true));
        Assert.Equal(1, reader.Calls);
        var report = ReportRenderer.Parse(writer.Json!);
        Assert.Empty(report.Actions);
        Assert.Empty(report.StabilityTests);
        if (scenario is "interrupted" or "corrupt")
        {
            var record = Assert.Single(report.TemporaryCleanup);
            Assert.Equal("Interrupted", record.Status);
            Assert.Equal(recovery.AttemptId, record.JournalAttemptId);
            Assert.Equal(1, record.DeletedFiles);
            Assert.Equal(TemporaryFileOutcome.Unverified, record.Files[1].Outcome);
            Assert.Equal("third", Assert.Single(record.NotAttemptedCandidates));
            Assert.Contains("no intentados: 1", writer.Text);
            Assert.Contains("No se reanuda automáticamente", output.ToString());
            Assert.DoesNotContain("No se registraron acciones externas ni mantenimiento", writer.Text);
            if (scenario == "corrupt") Assert.Contains("datos corruptos", output.ToString());
        }
        else
        {
            Assert.Empty(report.TemporaryCleanup);
            if (scenario == "unreadable")
            {
                Assert.Contains(report.Notes, note => note.Contains("no se presume ausencia de cambios", StringComparison.Ordinal));
                Assert.Contains("La ejecución sigue bloqueada", output.ToString());
            }
            else Assert.DoesNotContain("registro de limpieza incompleto", output.ToString());
        }
    }

    private sealed class Reader(string scenario, TemporaryCleanupRecovery recovery) : ITemporaryCleanupRecoveryReader
    {
        public int Calls { get; private set; }
        public Task<IReadOnlyList<TemporaryCleanupRecovery>> ReadAllAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            if (scenario == "unreadable") throw new IOException("Synthetic journal read failure");
            return Task.FromResult<IReadOnlyList<TemporaryCleanupRecovery>>([recovery]);
        }
    }

    private sealed class Writer : IReportFileWriter
    {
        public string? Json { get; private set; }
        public string? Text { get; private set; }
        public Task WriteAsync(string path, string content, CancellationToken cancellationToken)
        {
            if (Path.GetExtension(path) == ".json") Json = content;
            else Text = content;
            return Task.CompletedTask;
        }
    }

    private sealed class NoWorkStore : IStabilitySessionStore
    {
        public Task<InterruptedStabilitySession?> ReadInterruptedAsync(CancellationToken cancellationToken) =>
            Task.FromResult<InterruptedStabilitySession?>(null);
        public Task StartAsync(DateTimeOffset startedAt, CancellationToken cancellationToken) => throw new InvalidOperationException("No load allowed");
        public Task UpdateAsync(string completedProbeId, CancellationToken cancellationToken) => throw new InvalidOperationException("No load allowed");
        public Task CompleteAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("No load allowed");
    }
}
