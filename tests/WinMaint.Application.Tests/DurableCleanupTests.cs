using WinMaint.Domain;
using WinMaint.Reporting;

namespace WinMaint.Application.Tests;

internal sealed class RecordingCleanupJournal : ITemporaryCleanupJournal
{
    public List<TemporaryCleanupCheckpoint> Checkpoints { get; } = [];
    public int? FailSequence { get; init; }
    public Action<TemporaryCleanupCheckpoint>? OnAppend { get; init; }
    public Task AppendAsync(TemporaryCleanupCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        if (checkpoint.Sequence == FailSequence) throw new IOException("Synthetic checkpoint failure");
        Checkpoints.Add(checkpoint);
        OnAppend?.Invoke(checkpoint);
        return Task.CompletedTask;
    }
}

public sealed class DurableCleanupTests
{
    [Theory]
    [InlineData(0, 0, "Blocked")]
    [InlineData(1, 0, "Interrupted")]
    [InlineData(2, 1, "Interrupted")]
    [InlineData(3, 1, "Interrupted")]
    [InlineData(4, 2, "Interrupted")]
    [InlineData(5, 2, "Interrupted")]
    public async Task PersistenceFailureStopsAtExactBoundary(int failure, int calls, string status)
    {
        var journal = new RecordingCleanupJournal { FailSequence = failure };
        var runner = new Runner(journal);
        var plan = Plan();
        var result = await new TemporaryCleanupCoordinator(PreparedCleanupTestContext.Bind(runner, plan), true, journal: journal).ExecuteAsync(plan, plan.Confirmation);
        Assert.Equal(calls, runner.Calls);
        Assert.Equal(status, result.Status);
        if (failure is 2 or 4)
            Assert.Equal(TemporaryFileOutcome.Unverified, result.Files[^1].Outcome);
        Assert.Equal(2 - calls, result.NotAttemptedCandidates.Count);
    }

    [Fact]
    public async Task EnabledCoordinatorWithoutJournalDoesNotInvokeRunner()
    {
        var journal = new RecordingCleanupJournal();
        var runner = new Runner(journal);
        var plan = Plan();
        Assert.Equal("Blocked", (await new TemporaryCleanupCoordinator(PreparedCleanupTestContext.Bind(runner, plan), true).ExecuteAsync(plan, plan.Confirmation)).Status);
        Assert.Equal(0, runner.Calls);
    }

    [Fact]
    public async Task CancellationDuringIntentFlushDoesNotInvokeAdapter()
    {
        using var cancelled = new CancellationTokenSource();
        var journal = new RecordingCleanupJournal
        {
            OnAppend = checkpoint =>
        { if (checkpoint.Kind == TemporaryCleanupCheckpointKind.Intent) cancelled.Cancel(); }
        };
        var runner = new Runner(journal);
        var plan = Plan();
        var result = await new TemporaryCleanupCoordinator(PreparedCleanupTestContext.Bind(runner, plan), true, journal: journal)
            .ExecuteAsync(plan, plan.Confirmation, cancelled.Token);
        Assert.Equal(0, runner.Calls);
        Assert.Equal("Cancelled", result.Status);
        Assert.Equal(TemporaryFileOutcome.Cancelled, Assert.Single(result.Files).Outcome);
        Assert.Single(result.NotAttemptedCandidates);
    }

    [Theory]
    [InlineData(1, 0, 0, 2, false)]
    [InlineData(2, 0, 1, 1, false)]
    [InlineData(3, 1, 0, 1, false)]
    [InlineData(4, 1, 1, 0, false)]
    [InlineData(5, 2, 0, 0, false)]
    [InlineData(6, 2, 0, 0, true)]
    public async Task RecoveryDistinguishesKnownUncertainAndNotAttemptedWithoutInvocations(
        int retained, int deleted, int uncertain, int untouched, bool closed)
    {
        var journal = new RecordingCleanupJournal();
        var runner = new Runner(journal);
        var plan = Plan();
        await new TemporaryCleanupCoordinator(PreparedCleanupTestContext.Bind(runner, plan), true, journal: journal).ExecuteAsync(plan, plan.Confirmation);
        var recovery = TemporaryCleanupRecoveryEngine.Replay(journal.Checkpoints[0].AttemptId, journal.Checkpoints.Take(retained).ToArray());
        Assert.Equal(deleted, recovery.Files.Count(file => file.Outcome == TemporaryFileOutcome.DeletedVerified));
        Assert.Equal(uncertain, recovery.Files.Count(file => file.Outcome == TemporaryFileOutcome.Unverified));
        Assert.Equal(untouched, recovery.NotAttempted.Count);
        Assert.Equal(closed, recovery.IsClosed);
        Assert.False(recovery.IsCorrupt);
        Assert.Equal(2, runner.Calls); // Replay has no runner and cannot resume the operation.
    }

    [Fact]
    public async Task CorruptTailDoesNotClaimRemainingCandidatesWereUntouched()
    {
        var journal = new RecordingCleanupJournal();
        var runner = new Runner(journal);
        var plan = Plan();
        await new TemporaryCleanupCoordinator(PreparedCleanupTestContext.Bind(runner, plan), true, journal: journal).ExecuteAsync(plan, plan.Confirmation);
        var recovery = TemporaryCleanupRecoveryEngine.Replay(journal.Checkpoints[0].AttemptId,
            journal.Checkpoints.Take(3).ToArray(), corruptTail: true);
        Assert.True(recovery.IsCorrupt);
        Assert.False(recovery.IsClosed);
        Assert.Empty(recovery.NotAttempted);
        Assert.Equal(TemporaryFileOutcome.Unverified, recovery.Files[1].Outcome);
        Assert.Equal(1, recovery.ToRecord()!.DeletedFiles);
    }

    [Fact]
    public async Task FileJournalReopensAndDetectsTornTailWithoutNativeDeletion()
    {
        string directory = Path.Combine(Path.GetTempPath(), "WinMaintJournalTests", Guid.NewGuid().ToString("N"));
        var recording = new RecordingCleanupJournal();
        var runner = new Runner(recording);
        var plan = Plan();
        await new TemporaryCleanupCoordinator(PreparedCleanupTestContext.Bind(runner, plan), true, journal: recording).ExecuteAsync(plan, plan.Confirmation);
        try
        {
            using (var store = new FileTemporaryCleanupJournal(directory))
                foreach (var checkpoint in recording.Checkpoints.Take(4))
                    await store.AppendAsync(checkpoint, CancellationToken.None);
            using var reopened = new FileTemporaryCleanupJournal(directory);
            var recovered = Assert.Single(await reopened.ReadAllAsync());
            Assert.False(recovered.IsClosed);
            Assert.False(recovered.IsCorrupt);
            Assert.Equal(1, recovered.ToRecord()!.DeletedFiles);
            Assert.Equal(TemporaryFileOutcome.Unverified, recovered.Files[1].Outcome);
            string broken = Path.Combine(directory, recording.Checkpoints[0].AttemptId.ToString("N"), "00000003.json");
            await File.WriteAllTextAsync(broken, "{torn");
            var corrupt = Assert.Single(await reopened.ReadAllAsync());
            Assert.True(corrupt.IsCorrupt);
            Assert.False(corrupt.IsClosed);
            Assert.Empty(corrupt.NotAttempted);
            Assert.Equal(1, corrupt.ToRecord()!.DeletedFiles);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task ReadOnlyStartupDoesNotCreateJournalDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "WinMaintJournalTests", Guid.NewGuid().ToString("N"));
        using var journal = new FileTemporaryCleanupJournal(directory);
        Assert.Empty(await journal.ReadAllAsync());
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task ReopenedStoreCannotAppendToOrOverwriteExistingAttempt()
    {
        var directory = Path.Combine(Path.GetTempPath(), "WinMaintJournalTests", Guid.NewGuid().ToString("N"));
        var recording = new RecordingCleanupJournal();
        var plan = Plan();
        await new TemporaryCleanupCoordinator(PreparedCleanupTestContext.Bind(new Runner(recording), plan), true, journal: recording).ExecuteAsync(plan, plan.Confirmation);
        try
        {
            using (var journal = new FileTemporaryCleanupJournal(directory))
                await journal.AppendAsync(recording.Checkpoints[0], CancellationToken.None);
            var path = Path.Combine(directory, recording.Checkpoints[0].AttemptId.ToString("N"), "00000000.json");
            var before = await File.ReadAllBytesAsync(path);
            using var reopened = new FileTemporaryCleanupJournal(directory);
            await Assert.ThrowsAsync<IOException>(() => reopened.AppendAsync(recording.Checkpoints[0], CancellationToken.None));
            await Assert.ThrowsAsync<IOException>(() => reopened.AppendAsync(recording.Checkpoints[1], CancellationToken.None));
            Assert.Equal(before, await File.ReadAllBytesAsync(path));
            var recovery = Assert.Single(await reopened.ReadAllAsync());
            Assert.False(recovery.IsClosed);
            Assert.Equal(2, recovery.NotAttempted.Count);
            Assert.Empty(recovery.Files);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    private static TemporaryCleanupPlan Plan()
    {
        var now = DateTimeOffset.UtcNow;
        return PreparedCleanupTestContext.Authorize(new(new(Guid.NewGuid(), now,
            EvidenceCoverage.Complete, 2, 36, 0, 0, 0), true, ["first", "second"]), now);
    }

    private sealed class Runner(RecordingCleanupJournal journal) : ITemporaryCleanupRunner
    {
        public int Calls { get; private set; }
        public Task<TemporaryFileResult> RunAsync(string candidateId, CancellationToken cancellationToken)
        {
            Assert.Equal(TemporaryCleanupCheckpointKind.Intent, journal.Checkpoints[^1].Kind);
            Assert.Equal(candidateId, journal.Checkpoints[^1].CandidateId);
            Assert.Equal(TemporaryCleanupCheckpointKind.Started, journal.Checkpoints[0].Kind);
            Calls++;
            return Task.FromResult(new TemporaryFileResult(candidateId, TemporaryFileOutcome.DeletedVerified, 18));
        }
    }
}
