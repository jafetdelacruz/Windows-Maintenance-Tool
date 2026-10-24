using WinMaint.Domain;

namespace WinMaint.Application.Tests;

public sealed class CleanupDiagnosticBindingTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddDays(100);

    [Theory]
    [InlineData("boolean-only")]
    [InlineData("no-session")]
    [InlineData("other-session")]
    [InlineData("other-inventory")]
    [InlineData("not-readonly")]
    [InlineData("empty")]
    [InlineData("expired")]
    [InlineData("after-preview")]
    [InlineData("unsupported")]
    [InlineData("absent-platform")]
    [InlineData("invalid-platform-coverage")]
    [InlineData("duplicate-platform")]
    [InlineData("other-volume")]
    [InlineData("absent-volume")]
    [InlineData("invalid-volume-coverage")]
    [InlineData("nan-volume")]
    [InlineData("range-volume")]
    [InlineData("p0")]
    [InlineData("disk-warning")]
    [InlineData("prior-memory-failure")]
    public async Task InvalidDiagnosticBlocksBeforeJournalAndRunner(string reason)
    {
        var plan = Plan();
        var evidence = plan.DiagnosticEvidence!;
        var data = evidence.Snapshot.Sections[0].Data.ToList();
        switch (reason)
        {
            case "boolean-only": plan = plan with { DiagnosticEvidence = null }; break;
            case "no-session": plan = plan with { SessionId = Guid.Empty }; break;
            case "other-session": evidence = evidence with { SessionId = Guid.NewGuid() }; break;
            case "other-inventory": evidence = evidence with { InventoryId = Guid.NewGuid() }; break;
            case "not-readonly": evidence = evidence with { Snapshot = evidence.Snapshot with { IsReadOnly = false } }; break;
            case "empty": data.Clear(); break;
            case "expired": evidence = evidence with { Snapshot = evidence.Snapshot with { CapturedAt = Now.AddMinutes(-6) } }; break;
            case "after-preview": evidence = evidence with { Snapshot = evidence.Snapshot with { CapturedAt = Now.AddSeconds(1) } }; break;
            case "unsupported": data[0] = data[0] with { TextValue = "unsupported" }; break;
            case "absent-platform": data[0] = data[0] with { Coverage = EvidenceCoverage.Absent }; break;
            case "invalid-platform-coverage": data[0] = data[0] with { Coverage = (EvidenceCoverage)999 }; break;
            case "duplicate-platform": data.Add(data[0]); break;
            case "other-volume": data[1] = data[1] with { Name = @"Volumen D:\" }; break;
            case "absent-volume": data[1] = data[1] with { Coverage = EvidenceCoverage.Absent }; break;
            case "invalid-volume-coverage": data[1] = data[1] with { Coverage = (EvidenceCoverage)999 }; break;
            case "nan-volume": data[1] = data[1] with { NumericValue = double.NaN }; break;
            case "range-volume": data[1] = data[1] with { NumericValue = 101 }; break;
            case "p0": data.Add(DiagnosticDatum.Available("Disk", "Unhealthy", "Fixture", metricKey: "storage.disk.health", textValue: "Unhealthy")); break;
            case "disk-warning": data.Add(DiagnosticDatum.Available("Disk", "Warning", "Fixture", metricKey: "storage.disk.health", textValue: "Warning")); break;
            case "prior-memory-failure": data.Add(DiagnosticDatum.Available("Memory", "failed", "Fixture", metricKey: "memory.diagnostic.previous.status", textValue: "failed")); break;
        }
        evidence = evidence with { Snapshot = evidence.Snapshot with { Sections = [evidence.Snapshot.Sections[0] with { Data = data }] } };
        if (reason != "boolean-only") plan = plan with { DiagnosticEvidence = evidence };
        var runner = new Runner();
        var journal = new RecordingCleanupJournal();
        var result = await new TemporaryCleanupCoordinator(PreparedCleanupTestContext.Bind(runner, plan), true, () => Now, journal)
            .ExecuteAsync(plan, plan.Confirmation);
        Assert.Equal("Blocked", result.Status);
        Assert.Equal(0, runner.Calls);
        Assert.Empty(journal.Checkpoints);
    }

    [Theory]
    [InlineData("inventory")]
    [InlineData("order")]
    [InlineData("unprepared")]
    public async Task PlanCannotSubstitutePreparedMetadataOrCandidateOrder(string reason)
    {
        var original = Plan();
        var runner = new Runner();
        ITemporaryCleanupRunner bound = reason == "unprepared" ? runner : PreparedCleanupTestContext.Bind(runner, original);
        var plan = reason switch
        {
            "inventory" => original with { Inventory = original.Inventory with { CandidateLogicalBytes = 99 } },
            "order" => original with { CandidateIds = original.CandidateIds.Reverse().ToArray() },
            _ => original,
        };
        var journal = new RecordingCleanupJournal();
        var result = await new TemporaryCleanupCoordinator(bound, true, () => Now, journal).ExecuteAsync(plan, plan.Confirmation);
        Assert.Equal("Blocked", result.Status);
        Assert.Contains("mapa del ejecutor", result.Detail);
        Assert.Equal(0, runner.Calls);
        Assert.Empty(journal.Checkpoints);
    }

    [Fact]
    public async Task DiagnosticExpiryBetweenFilesStopsWhileInventoryIsStillFresh()
    {
        var now = Now;
        var plan = Plan();
        plan = plan with
        {
            DiagnosticEvidence = plan.DiagnosticEvidence! with
            { Snapshot = plan.DiagnosticEvidence.Snapshot with { CapturedAt = now.AddMinutes(-4) } }
        };
        var runner = new Runner(() => now = now.AddMinutes(2));
        var journal = new RecordingCleanupJournal();
        var result = await new TemporaryCleanupCoordinator(PreparedCleanupTestContext.Bind(runner, plan), true, () => now, journal)
            .ExecuteAsync(plan, plan.Confirmation);
        Assert.Equal("Interrupted", result.Status);
        Assert.Equal(1, runner.Calls);
        Assert.Equal(1, result.DeletedFiles);
        Assert.Single(result.NotAttemptedCandidates);
        Assert.Equal(TemporaryCleanupCheckpointKind.Finished, journal.Checkpoints[^1].Kind);
    }

    private static TemporaryCleanupPlan Plan() => PreparedCleanupTestContext.Authorize(new(
        new(Guid.NewGuid(), Now, EvidenceCoverage.Complete, 2, 36, 0, 0, 0), true, ["first", "second"]), Now);

    [Fact]
    public async Task DiagnosticExpiryDuringIntentFlushDoesNotInvokeRunner()
    {
        var now = Now;
        var plan = Plan();
        plan = plan with
        {
            DiagnosticEvidence = plan.DiagnosticEvidence! with
            { Snapshot = plan.DiagnosticEvidence.Snapshot with { CapturedAt = now.AddMinutes(-4) } }
        };
        var journal = new RecordingCleanupJournal
        {
            OnAppend = checkpoint =>
        { if (checkpoint.Kind == TemporaryCleanupCheckpointKind.Intent) now = Now.AddMinutes(2); }
        };
        var runner = new Runner();
        var result = await new TemporaryCleanupCoordinator(PreparedCleanupTestContext.Bind(runner, plan), true, () => now, journal)
            .ExecuteAsync(plan, plan.Confirmation);
        Assert.Equal("Interrupted", result.Status);
        Assert.Equal(0, runner.Calls);
        Assert.Equal(TemporaryFileOutcome.BlockedStale, Assert.Single(result.Files).Outcome);
        Assert.Single(result.NotAttemptedCandidates);
        Assert.Equal(TemporaryCleanupCheckpointKind.Finished, journal.Checkpoints[^1].Kind);
    }

    private sealed class Runner(Action? after = null) : ITemporaryCleanupRunner
    {
        public int Calls { get; private set; }
        public Task<TemporaryFileResult> RunAsync(string candidateId, CancellationToken cancellationToken)
        {
            Calls++;
            after?.Invoke();
            return Task.FromResult(new TemporaryFileResult(candidateId, TemporaryFileOutcome.DeletedVerified, 18));
        }
    }
}
