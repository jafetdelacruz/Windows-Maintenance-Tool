using WinMaint.Domain;

namespace WinMaint.Application;

public enum TemporaryCleanupCheckpointKind { Started, Intent, Outcome, Finished }

// Acknowledgement means the checkpoint has been flushed. Fakes prove ordering, not durability.
public interface ITemporaryCleanupJournal
{
    Task AppendAsync(TemporaryCleanupCheckpoint checkpoint, CancellationToken cancellationToken);
}

public interface ITemporaryCleanupRecoveryReader
{
    Task<IReadOnlyList<TemporaryCleanupRecovery>> ReadAllAsync(CancellationToken cancellationToken = default);
}

public sealed record TemporaryCleanupCheckpoint(Guid AttemptId, int Sequence, TemporaryCleanupCheckpointKind Kind,
    TemporaryInventory? Inventory = null, IReadOnlyList<string>? CandidateIds = null,
    string? CandidateId = null, TemporaryFileResult? Result = null, string? Status = null,
    Guid? DiagnosticSessionId = null, DateTimeOffset? DiagnosticCapturedAt = null);

public sealed record TemporaryCleanupRecovery(Guid AttemptId, TemporaryInventory? Inventory,
    bool IsClosed, bool IsCorrupt, string? ClosedStatus, IReadOnlyList<TemporaryFileResult> Files,
    IReadOnlyList<string> NotAttempted, Guid? DiagnosticSessionId = null, DateTimeOffset? DiagnosticCapturedAt = null)
{
    public TemporaryCleanupRecord? ToRecord() => Inventory is null ? null :
        new(Inventory, IsClosed ? ClosedStatus! : "Interrupted", Files,
            "Recuperación de registro durable; no se reanuda ni se presume reversión.")
        {
            JournalAttemptId = AttemptId,
            NotAttemptedCandidates = NotAttempted,
            DiagnosticSessionId = DiagnosticSessionId,
            DiagnosticCapturedAt = DiagnosticCapturedAt
        };
}

public static class TemporaryCleanupRecoveryEngine
{
    public static TemporaryCleanupRecovery Replay(Guid attemptId,
        IReadOnlyList<TemporaryCleanupCheckpoint> checkpoints, bool corruptTail = false)
    {
        ArgumentNullException.ThrowIfNull(checkpoints);
        var files = new List<TemporaryFileResult>();
        var attempted = new HashSet<string>(StringComparer.Ordinal);
        TemporaryInventory? inventory = null;
        Guid? diagnosticSession = null;
        DateTimeOffset? diagnosticCapturedAt = null;
        IReadOnlyList<string> planned = [];
        string? pending = null;
        string? closedStatus = null;
        var corrupt = false;
        for (var index = 0; index < checkpoints.Count; index++)
        {
            var checkpoint = checkpoints[index];
            if (checkpoint is null || checkpoint.AttemptId != attemptId || checkpoint.Sequence != index || closedStatus is not null)
            { corrupt = true; break; }
            if (index == 0)
            {
                if (checkpoint.Kind != TemporaryCleanupCheckpointKind.Started || checkpoint.Inventory is null ||
                    checkpoint.Inventory.Id == Guid.Empty || checkpoint.Inventory.Coverage != EvidenceCoverage.Complete ||
                    checkpoint.Inventory.CandidateFiles is null or < 0 or > 20_000 ||
                    checkpoint.Inventory.CandidateLogicalBytes is null or < 0 ||
                    checkpoint.Inventory.RecentFiles is null or < 0 || checkpoint.Inventory.ProtectedItems is null or < 0 ||
                    checkpoint.Inventory.UnavailableItems != 0 ||
                    checkpoint.DiagnosticSessionId is null || checkpoint.DiagnosticSessionId == Guid.Empty ||
                    checkpoint.DiagnosticCapturedAt is null || checkpoint.DiagnosticCapturedAt == default(DateTimeOffset) ||
                    checkpoint.DiagnosticCapturedAt > checkpoint.Inventory.CapturedAt ||
                    checkpoint.Inventory.CapturedAt - checkpoint.DiagnosticCapturedAt > TimeSpan.FromMinutes(5) ||
                    checkpoint.CandidateIds is null || checkpoint.CandidateIds.Any(id => !IsOpaqueId(id)) ||
                    checkpoint.CandidateIds.Count != checkpoint.Inventory.CandidateFiles ||
                    checkpoint.CandidateIds.Distinct(StringComparer.Ordinal).Count() != checkpoint.CandidateIds.Count)
                { corrupt = true; break; }
                inventory = checkpoint.Inventory;
                diagnosticSession = checkpoint.DiagnosticSessionId;
                diagnosticCapturedAt = checkpoint.DiagnosticCapturedAt;
                planned = checkpoint.CandidateIds;
                continue;
            }
            switch (checkpoint.Kind)
            {
                case TemporaryCleanupCheckpointKind.Intent when pending is null &&
                    attempted.Count < planned.Count && checkpoint.CandidateId == planned[attempted.Count]:
                    pending = checkpoint.CandidateId;
                    attempted.Add(pending!);
                    break;
                case TemporaryCleanupCheckpointKind.Outcome when pending is not null &&
                    checkpoint.Result is { } result && result.CandidateId == pending && IsValidResult(result):
                    if (result.Outcome == TemporaryFileOutcome.DeletedVerified &&
                        result.DeletedLogicalBytes > inventory!.CandidateLogicalBytes - files.Sum(file => file.DeletedLogicalBytes ?? 0))
                    { corrupt = true; break; }
                    files.Add(result);
                    pending = null;
                    break;
                case TemporaryCleanupCheckpointKind.Finished when pending is null && checkpoint.Status is
                    "Completed" or "FinishedWithSkips" or "Cancelled" or "Failed" or "Interrupted":
                    if (checkpoint.Status is "Completed" or "FinishedWithSkips" && attempted.Count != planned.Count)
                    { corrupt = true; break; }
                    if (checkpoint.Status == "Completed" && files.Any(file => file.Outcome != TemporaryFileOutcome.DeletedVerified))
                    { corrupt = true; break; }
                    closedStatus = checkpoint.Status;
                    break;
                default:
                    corrupt = true;
                    break;
            }
            if (corrupt) break;
        }
        corrupt |= corruptTail || inventory is null;
        if (pending is not null) files.Add(new(pending, TemporaryFileOutcome.Unverified, null));
        // A corrupt suffix may hide additional intents/outcomes. Never call those files untouched.
        if (corrupt)
            foreach (var id in planned.Except(files.Select(file => file.CandidateId), StringComparer.Ordinal))
                files.Add(new(id, TemporaryFileOutcome.Unverified, null));
        return new(attemptId, inventory, closedStatus is not null && !corrupt, corrupt, closedStatus,
            files, corrupt ? [] : planned.Except(attempted, StringComparer.Ordinal).ToArray(), diagnosticSession, diagnosticCapturedAt);
    }

    public static bool IsOpaqueId(string? id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 128 &&
        id is not ("." or "..") && id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    public static bool IsValidResult(TemporaryFileResult result) => Enum.IsDefined(result.Outcome) &&
        result.NativeError is null or >= 0 &&
        (result.Outcome == TemporaryFileOutcome.DeletedVerified ? result.DeletedLogicalBytes is >= 0 :
            result.DeletedLogicalBytes is null or 0);
}
