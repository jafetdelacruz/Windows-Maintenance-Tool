using System.Text.Json;
using WinMaint.Domain;
using WinMaint.Reporting;

namespace WinMaint.Application.Tests;

public sealed class TemporaryCleanupIntegrationTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddDays(100);

    [Theory]
    [InlineData("production")]
    [InlineData("diagnostic")]
    [InlineData("partial")]
    [InlineData("missing")]
    [InlineData("expired")]
    [InlineData("future")]
    [InlineData("duplicate")]
    public async Task UnsafePlanNeverInvokesRunner(string reason)
    {
        var plan = Plan();
        plan = reason switch
        {
            "diagnostic" => plan with { HasDiagnostic = false },
            "partial" => plan with { Inventory = plan.Inventory with { Coverage = EvidenceCoverage.Partial } },
            "missing" => plan with { Inventory = plan.Inventory with { CandidateFiles = null } },
            "expired" => plan with { Inventory = plan.Inventory with { CapturedAt = Now.AddMinutes(-6) } },
            "future" => plan with { Inventory = plan.Inventory with { CapturedAt = Now.AddSeconds(1) } },
            "duplicate" => plan with { CandidateIds = ["first", "first"] },
            _ => plan,
        };
        var runner = new Runner();
        var coordinator = new TemporaryCleanupCoordinator(PreparedCleanupTestContext.Bind(runner, plan), reason != "production", () => Now, new RecordingCleanupJournal());
        Assert.Equal("Blocked", (await coordinator.ExecuteAsync(plan, plan.Confirmation)).Status);
        Assert.Equal(0, runner.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("DELETE LAB FILES")]
    public async Task WrongConfirmationDoesNotAuthorizeAnotherInventory(string? confirmation)
    {
        var runner = new Runner();
        var plan = Plan();
        var result = await new TemporaryCleanupCoordinator(PreparedCleanupTestContext.Bind(runner, plan), true, () => Now, new RecordingCleanupJournal()).ExecuteAsync(plan, confirmation);
        Assert.Equal("Cancelled", result.Status);
        Assert.Equal(0, runner.Calls);
        Assert.Equal(0, result.DeletedFiles);
    }

    [Fact]
    public async Task CancellationBetweenFilesKeepsVerifiedResults()
    {
        using var cancellation = new CancellationTokenSource();
        var runner = new Runner(id => { cancellation.Cancel(); return new(id, TemporaryFileOutcome.DeletedVerified, 18); });
        var plan = Plan();
        var result = await new TemporaryCleanupCoordinator(PreparedCleanupTestContext.Bind(runner, plan), true, () => Now, new RecordingCleanupJournal())
            .ExecuteAsync(plan, plan.Confirmation, cancellation.Token);
        Assert.Equal("Cancelled", result.Status);
        Assert.Equal(1, runner.Calls);
        Assert.Equal(1, result.DeletedFiles);
        Assert.Equal(18, result.DeletedLogicalBytes);
    }

    [Fact]
    public async Task AdapterInterruptionIsUnverifiedRatherThanZeroChanges()
    {
        var runner = new Runner(id => id == "first" ? new(id, TemporaryFileOutcome.DeletedVerified, 18)
            : throw new OperationCanceledException());
        var plan = Plan();
        var result = await new TemporaryCleanupCoordinator(PreparedCleanupTestContext.Bind(runner, plan), true, () => Now, new RecordingCleanupJournal()).ExecuteAsync(plan, plan.Confirmation);
        Assert.Equal("Interrupted", result.Status);
        Assert.Equal(1, result.DeletedFiles);
        Assert.Equal(TemporaryFileOutcome.Unverified, result.Files[1].Outcome);
        Assert.Null(result.Files[1].DeletedLogicalBytes);
        var text = ReportRenderer.ToText(Session(result));
        Assert.Contains("operaciones sin verificación", text);
        Assert.DoesNotContain("No se registraron acciones externas", text);
    }

    [Fact]
    public async Task InvalidAdapterResultStopsAndDoesNotCountDeletion()
    {
        var runner = new Runner(_ => new("different", TemporaryFileOutcome.DeletedVerified, 18));
        var plan = Plan();
        var result = await new TemporaryCleanupCoordinator(PreparedCleanupTestContext.Bind(runner, plan), true, () => Now, new RecordingCleanupJournal()).ExecuteAsync(plan, plan.Confirmation);
        Assert.Equal("Failed", result.Status);
        Assert.Equal(0, result.DeletedFiles);
        Assert.Equal(1, runner.Calls);
        Assert.Equal(TemporaryFileOutcome.Unverified, Assert.Single(result.Files).Outcome);
    }

    [Fact]
    public void InventoryIsStructuredAndUnknownDoesNotBecomeZero()
    {
        var journal = new ReportJournal("unused-no-files-written");
        journal.AddTemporaryInventory(new("temporary-inventory", "Inventario", EvidenceCoverage.Absent, [], []));
        var report = ReportRenderer.Parse(ReportRenderer.ToJson(journal.CreateReport("Partial")));
        Assert.Equal("1.2", report.SchemaVersion);
        var inventory = Assert.Single(report.TemporaryCleanup);
        Assert.Null(inventory.Inventory.CandidateFiles);
        Assert.Equal("InventoryOnly", inventory.Status);
        Assert.Contains("desconocido", ReportRenderer.ToText(report));
        Assert.Equal(0, inventory.DeletedFiles);
    }

    [Fact]
    public void SchemaOneRemainsReadableButCannotHideNewCleanupResults()
    {
        var legacy = Session(null) with { SchemaVersion = "1.0" };
        Assert.Empty(ReportRenderer.Parse(ReportRenderer.ToJson(legacy)).TemporaryCleanup);
        var result = new TemporaryCleanupRecord(Plan().Inventory, "Blocked", [], "No execution");
        Assert.Throws<JsonException>(() => ReportRenderer.Parse(ReportRenderer.ToJson(legacy with { TemporaryCleanup = [result] })));
    }

    [Fact]
    public async Task ValidatedSkipsRemainSeparateFromVerifiedDeletionsInReports()
    {
        var runner = new Runner(id => id == "first" ? new(id, TemporaryFileOutcome.DeletedVerified, 18)
            : new(id, TemporaryFileOutcome.SkippedBusy, 0, 32));
        var plan = Plan();
        var result = await new TemporaryCleanupCoordinator(PreparedCleanupTestContext.Bind(runner, plan), true, () => Now, new RecordingCleanupJournal()).ExecuteAsync(plan, plan.Confirmation);
        var parsed = ReportRenderer.Parse(ReportRenderer.ToJson(Session(result)));
        var saved = Assert.Single(parsed.TemporaryCleanup);
        Assert.Equal("FinishedWithSkips", saved.Status);
        Assert.Equal(1, saved.DeletedFiles);
        Assert.Equal(18, saved.DeletedLogicalBytes);
        Assert.Equal(32, saved.Files[1].NativeError);
        Assert.Contains("Espacio físico no medido", ReportRenderer.ToText(parsed));
    }

    [Fact]
    public void SchemaOnePointOneWithoutExplicitCleanupCollectionIsRejected()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(ReportRenderer.ToJson(Session(null)))!.AsObject();
        node.Remove("temporaryCleanup");
        Assert.Throws<JsonException>(() => ReportRenderer.Parse(node.ToJsonString()));
    }

    private static TemporaryCleanupPlan Plan() => PreparedCleanupTestContext.Authorize(new(new(Guid.NewGuid(), Now, EvidenceCoverage.Complete,
        2, 36, 0, 0, 0), true, ["first", "second"]), Now);

    private static SessionReport Session(TemporaryCleanupRecord? record) => new("1.1", Guid.Empty,
        "Test", "Fixture", "Windows Fixture", Now, Now, "Partial", [], [], [], [], [])
    { TemporaryCleanup = record is null ? [] : [record] };

    private sealed class Runner(Func<string, TemporaryFileResult>? run = null) : ITemporaryCleanupRunner
    {
        public int Calls { get; private set; }
        public Task<TemporaryFileResult> RunAsync(string candidateId, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(run?.Invoke(candidateId) ?? new(candidateId, TemporaryFileOutcome.DeletedVerified, 18));
        }
    }
}
