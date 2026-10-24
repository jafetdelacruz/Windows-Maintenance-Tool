namespace WinMaint.Domain;

public sealed record TemporaryInventory(
    Guid Id, DateTimeOffset CapturedAt, EvidenceCoverage Coverage,
    long? CandidateFiles, long? CandidateLogicalBytes, long? RecentFiles,
    long? ProtectedItems, long? UnavailableItems);

public enum TemporaryFileOutcome
{
    DeletedVerified, SkippedBusy, SkippedChanged, SkippedProtected,
    BlockedScope, BlockedStale, Cancelled, Failed, Unverified,
}

// Opaque identifiers, never full user paths or file contents.
public sealed record TemporaryFileResult(string CandidateId, TemporaryFileOutcome Outcome,
    long? DeletedLogicalBytes, int? NativeError = null);

public sealed record TemporaryCleanupRecord(TemporaryInventory Inventory, string Status,
    IReadOnlyList<TemporaryFileResult> Files, string Detail)
{
    public Guid? JournalAttemptId { get; init; }
    public Guid? DiagnosticSessionId { get; init; }
    public DateTimeOffset? DiagnosticCapturedAt { get; init; }
    public IReadOnlyList<string> NotAttemptedCandidates { get; init; } = [];
    public long DeletedFiles => Files.LongCount(file => file.Outcome == TemporaryFileOutcome.DeletedVerified);
    public long DeletedLogicalBytes => Files.Where(file => file.Outcome == TemporaryFileOutcome.DeletedVerified)
        .Sum(file => file.DeletedLogicalBytes ?? 0);
    public string PhysicalSpaceFreed { get; } = "Not measured";
}
