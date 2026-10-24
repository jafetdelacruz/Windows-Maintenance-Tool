using System.Text.Json;
using WinMaint.Application;
using WinMaint.Domain;

namespace WinMaint.Reporting;

// Validate stored facts, not today's health rules. Derived JSON fields must agree with facts.
internal static class TemporaryCleanupReportValidation
{
    internal static void Validate(TemporaryCleanupRecord record, JsonElement raw)
    {
        var inventory = record.Inventory;
        if (record.DiagnosticSessionId.HasValue != record.DiagnosticCapturedAt.HasValue ||
            record.DiagnosticSessionId == Guid.Empty || record.DiagnosticCapturedAt == default(DateTimeOffset) ||
            record.DiagnosticCapturedAt > inventory.CapturedAt ||
            record.DiagnosticSessionId.HasValue && !record.JournalAttemptId.HasValue)
            throw new JsonException("Vínculo diagnóstico de limpieza inválido.");
        if (!Enum.IsDefined(inventory.Coverage) || inventory.Id == Guid.Empty || inventory.CapturedAt == default ||
            inventory.CandidateFiles < 0 || inventory.CandidateLogicalBytes < 0 || inventory.RecentFiles < 0 ||
            inventory.ProtectedItems < 0 || inventory.UnavailableItems < 0 ||
            inventory.Coverage == EvidenceCoverage.Complete &&
                (inventory.CandidateFiles is null || inventory.CandidateLogicalBytes is null ||
                 inventory.RecentFiles is null || inventory.ProtectedItems is null || inventory.UnavailableItems != 0) ||
            inventory.CandidateFiles == 0 && inventory.CandidateLogicalBytes > 0 ||
            record.NotAttemptedCandidates is null || record.JournalAttemptId == Guid.Empty)
            throw new JsonException("Inventario o identidad de recuperación inválidos.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        long bytes = 0;
        long deleted = 0;
        foreach (var file in record.Files)
        {
            if (!TemporaryCleanupRecoveryEngine.IsOpaqueId(file.CandidateId) || !ids.Add(file.CandidateId) ||
                !TemporaryCleanupRecoveryEngine.IsValidResult(file))
                throw new JsonException("Resultado de temporales inválido o duplicado.");
            if (file.Outcome != TemporaryFileOutcome.DeletedVerified) continue;
            var size = file.DeletedLogicalBytes!.Value;
            if (size > long.MaxValue - bytes) throw new JsonException("Suma de bytes fuera de rango.");
            bytes += size;
            deleted++;
        }
        foreach (var id in record.NotAttemptedCandidates)
            if (!TemporaryCleanupRecoveryEngine.IsOpaqueId(id) || !ids.Add(id))
                throw new JsonException("Los no intentados deben ser únicos y distintos de los resultados.");

        if (bytes > inventory.CandidateLogicalBytes || ids.Count > inventory.CandidateFiles ||
            record.JournalAttemptId.HasValue && ids.Count != inventory.CandidateFiles ||
            record.JournalAttemptId is null && record.NotAttemptedCandidates.Count != 0 ||
            record.Status is "InventoryOnly" or "Blocked" && record.Files.Count != 0 ||
            record.Status == "InventoryOnly" && record.JournalAttemptId.HasValue ||
            record.Status is "Completed" or "FinishedWithSkips" &&
                (inventory.Coverage != EvidenceCoverage.Complete || record.Files.Count != inventory.CandidateFiles ||
                 record.NotAttemptedCandidates.Count != 0) ||
            record.Status == "Completed" && deleted != record.Files.Count ||
            record.Status == "FinishedWithSkips" &&
                (deleted == record.Files.Count || record.Files.Any(file => file.Outcome is
                    TemporaryFileOutcome.Cancelled or TemporaryFileOutcome.Failed or TemporaryFileOutcome.Unverified)))
            throw new JsonException("Resultados de temporales inconsistentes con el plan o cierre.");

        CheckCounter(raw, "deletedFiles", deleted);
        CheckCounter(raw, "deletedLogicalBytes", bytes);
        if (raw.TryGetProperty("physicalSpaceFreed", out var physical) &&
            (physical.ValueKind != JsonValueKind.String || physical.GetString() != "Not measured"))
            throw new JsonException("El espacio físico no fue medido.");
    }

    private static void CheckCounter(JsonElement raw, string name, long expected)
    {
        if (raw.TryGetProperty(name, out var value) &&
            (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var actual) || actual != expected))
            throw new JsonException("Contador derivado incompatible con los resultados.");
    }
}
