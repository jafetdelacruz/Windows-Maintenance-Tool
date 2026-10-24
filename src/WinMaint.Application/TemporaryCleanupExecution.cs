using WinMaint.Domain;

namespace WinMaint.Application;

public sealed record TemporaryCleanupPlan(TemporaryInventory Inventory,
    bool HasDiagnostic, IReadOnlyList<string> CandidateIds)
{
    public string Confirmation => $"CLEAN TEMP {Inventory.Id:N}";
    public Guid SessionId { get; init; }
    public TemporaryCleanupDiagnosticEvidence? DiagnosticEvidence { get; init; }

    public static TemporaryCleanupPlan FromPrepared(IPreparedTemporaryCleanupRunner prepared,
        Guid sessionId, DiagnosticReport diagnostic) => new(prepared.Inventory, true, prepared.CandidateIds.ToArray())
        {
            SessionId = sessionId,
            DiagnosticEvidence = new(sessionId, prepared.Inventory.Id, diagnostic),
        };
}

public interface ITemporaryCleanupRunner
{
    Task<TemporaryFileResult> RunAsync(string candidateId, CancellationToken cancellationToken);
}

public interface IPreparedTemporaryCleanupRunner : ITemporaryCleanupRunner
{
    TemporaryInventory Inventory { get; }
    IReadOnlyList<string> CandidateIds { get; }
}

// No production composition registers a mutable runner. Native validation stays in the adapter.
public sealed class TemporaryCleanupCoordinator(ITemporaryCleanupRunner runner,
    bool executionEnabled = false, Func<DateTimeOffset>? utcNow = null, ITemporaryCleanupJournal? journal = null)
{
    public async Task<TemporaryCleanupRecord> ExecuteAsync(TemporaryCleanupPlan plan,
        string? confirmation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var inventory = plan.Inventory;
        var candidates = plan.CandidateIds.ToArray();
        var files = new List<TemporaryFileResult>();
        Guid? attempt = null;
        var sequence = 0;
        TemporaryCleanupRecord Result(string status, string detail) => new(inventory, status, files.ToArray(), detail)
        {
            JournalAttemptId = attempt,
            NotAttemptedCandidates = attempt.HasValue ? candidates.Skip(files.Count).ToArray() : [],
            DiagnosticSessionId = attempt.HasValue ? plan.SessionId : null,
            DiagnosticCapturedAt = attempt.HasValue ? plan.DiagnosticEvidence!.Snapshot.CapturedAt : null
        };
        bool Fresh() => (utcNow?.Invoke() ?? DateTimeOffset.UtcNow) - inventory.CapturedAt is var age &&
            age >= TimeSpan.Zero && age <= TimeSpan.FromMinutes(5);
        async Task<TemporaryCleanupRecord> FinishAsync(string status, string detail)
        {
            try
            {
                await journal!.AppendAsync(new(attempt!.Value, sequence++, TemporaryCleanupCheckpointKind.Finished,
                    Status: status), CancellationToken.None).ConfigureAwait(false);
                return Result(status, detail);
            }
            catch (Exception)
            { return Result("Interrupted", "No se pudo persistir el cierre; no se reanuda automáticamente."); }
        }

        if (!executionEnabled) return Result("Blocked", "Ejecución de producción bloqueada.");
        if (!plan.HasDiagnostic) return Result("Blocked", "Falta diagnóstico previo.");
        if (inventory.Coverage != EvidenceCoverage.Complete || inventory.Id == Guid.Empty ||
            inventory.CandidateFiles is null or < 0 || inventory.CandidateLogicalBytes is null or < 0 ||
            inventory.RecentFiles is null or < 0 || inventory.ProtectedItems is null or < 0 ||
            inventory.UnavailableItems != 0 || candidates.Length != inventory.CandidateFiles ||
            candidates.Length > 20_000 || candidates.Any(id => !TemporaryCleanupRecoveryEngine.IsOpaqueId(id)) ||
            candidates.Distinct(StringComparer.Ordinal).Count() != candidates.Length)
            return Result("Blocked", "Inventario incompleto o inconsistente.");
        if (!Fresh()) return Result("Blocked", "Inventario caducado o reloj incompatible.");
        if (runner is not IPreparedTemporaryCleanupRunner prepared || prepared.Inventory != inventory ||
            !prepared.CandidateIds.SequenceEqual(candidates, StringComparer.Ordinal))
            return Result("Blocked", "El plan no corresponde al inventario y mapa del ejecutor preparado.");
        if (!TemporaryCleanupDiagnosticPolicy.IsSufficient(plan, utcNow?.Invoke() ?? DateTimeOffset.UtcNow))
            return Result("Blocked", "Diagnóstico insuficiente, caducado o ajeno a esta sesión/inventario.");
        if (!string.Equals(confirmation, plan.Confirmation, StringComparison.Ordinal))
            return Result("Cancelled", "No se confirmó exactamente este inventario.");
        if (cancellationToken.IsCancellationRequested) return Result("Cancelled", "Cancelado antes de cualquier intento.");
        if (journal is null) return Result("Blocked", "Falta registro durable; no se invocó el ejecutor.");
        attempt = Guid.NewGuid();
        try
        {
            await journal.AppendAsync(new(attempt.Value, sequence++, TemporaryCleanupCheckpointKind.Started,
                inventory, candidates, DiagnosticSessionId: plan.SessionId,
                DiagnosticCapturedAt: plan.DiagnosticEvidence!.Snapshot.CapturedAt), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        { return Result("Blocked", "No se pudo persistir el inicio; no se invocó el ejecutor."); }

        foreach (var id in candidates)
        {
            if (cancellationToken.IsCancellationRequested)
                return await FinishAsync("Cancelled", "Cancelado; resultados conservados.").ConfigureAwait(false);
            if (!Fresh()) return await FinishAsync("Interrupted", "Inventario caducado durante la ejecución.").ConfigureAwait(false);
            if (!TemporaryCleanupDiagnosticPolicy.IsSufficient(plan, utcNow?.Invoke() ?? DateTimeOffset.UtcNow))
                return await FinishAsync("Interrupted", "La evidencia diagnóstica dejó de ser válida; no continúa el lote.").ConfigureAwait(false);
            try
            {
                await journal.AppendAsync(new(attempt.Value, sequence++, TemporaryCleanupCheckpointKind.Intent,
                    CandidateId: id), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            { return Result("Interrupted", "No se pudo persistir intención; este archivo no fue invocado y se detiene el lote."); }
            TemporaryFileResult file;
            var interrupted = false;
            var contextExpired = false;
            try
            {
                // A slow flush must not carry stale evidence across the mutation boundary.
                contextExpired = !cancellationToken.IsCancellationRequested &&
                    (!Fresh() || !TemporaryCleanupDiagnosticPolicy.IsSufficient(plan, utcNow?.Invoke() ?? DateTimeOffset.UtcNow));
                file = cancellationToken.IsCancellationRequested ? new(id, TemporaryFileOutcome.Cancelled, 0) :
                    contextExpired ? new(id, TemporaryFileOutcome.BlockedStale, 0) :
                    await runner.RunAsync(id, cancellationToken).ConfigureAwait(false);
                if (file is null || file.CandidateId != id || !TemporaryCleanupRecoveryEngine.IsValidResult(file) ||
                    (file.Outcome == TemporaryFileOutcome.DeletedVerified &&
                        file.DeletedLogicalBytes > inventory.CandidateLogicalBytes - files.Sum(item => item.DeletedLogicalBytes ?? 0)))
                    file = new(id, TemporaryFileOutcome.Unverified, null);
            }
            catch (Exception exception)
            {
                file = new(id, TemporaryFileOutcome.Unverified, null);
                interrupted = exception is OperationCanceledException;
            }
            files.Add(file);
            try
            {
                await journal.AppendAsync(new(attempt.Value, sequence++, TemporaryCleanupCheckpointKind.Outcome,
                    Result: file), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
                files[^1] = new(id, TemporaryFileOutcome.Unverified, null);
                return Result("Interrupted", "Persistencia perdida después del intento; puede haber cambios. No continúa el lote.");
            }
            if (file.Outcome == TemporaryFileOutcome.Cancelled)
                return await FinishAsync("Cancelled", "Cancelado; resultados conservados.").ConfigureAwait(false);
            if (contextExpired)
                return await FinishAsync("Interrupted", "La evidencia caducó durante la persistencia; no se invocó este archivo ni continúa el lote.").ConfigureAwait(false);
            if (file.Outcome is TemporaryFileOutcome.Failed or TemporaryFileOutcome.Unverified)
                return await FinishAsync(interrupted ? "Interrupted" : "Failed", "Intento fallido o sin verificación; resultados parciales conservados.").ConfigureAwait(false);
        }
        return await FinishAsync(files.Any(file => file.Outcome != TemporaryFileOutcome.DeletedVerified)
            ? "FinishedWithSkips" : "Completed", "Resultados durables por archivo; bytes lógicos, no espacio físico.").ConfigureAwait(false);
    }
}
