using WinMaint.Application;
using WinMaint.Domain;

namespace WinMaint.Infrastructure.Win;

internal sealed record TemporaryFileCandidate(
    string FullPath,
    long Length,
    DateTime LastWriteTimeUtc);

internal sealed record TemporaryFileInventory(
    string RootPath,
    DateTimeOffset CreatedAt,
    DateTimeOffset Cutoff,
    IReadOnlyList<TemporaryFileCandidate> Candidates,
    int SkippedItems);

internal sealed class TemporaryFileMaintenanceRunner : IMaintenanceActionRunner
{
    internal static readonly TimeSpan DefaultMinimumAge = TimeSpan.FromDays(1);
    internal static readonly TimeSpan MaximumInventoryAge = TimeSpan.FromMinutes(10);

    private readonly string temporaryRoot;
    private readonly string userProfileRoot;
    private readonly TimeSpan minimumAge;
    private readonly Func<DateTimeOffset> utcNow;
    private TemporaryFileInventory? inventory;

    public TemporaryFileMaintenanceRunner(
        string temporaryRoot,
        string userProfileRoot,
        TimeSpan? minimumAge = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        this.temporaryRoot = Path.GetFullPath(temporaryRoot);
        this.userProfileRoot = Path.GetFullPath(userProfileRoot);
        this.minimumAge = minimumAge ?? DefaultMinimumAge;
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);

        if (this.minimumAge < DefaultMinimumAge)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumAge),
                "La antigüedad mínima no puede ser menor a 24 horas.");
        }
    }

    public Task<MaintenanceActionResult> RunAsync(
        MaintenancePlanItem item,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Action.Id switch
        {
            "temp.inventory" => InventoryAsync(item.Action, cancellationToken),
            "temp.cleanup" => CleanupAsync(item.Action, cancellationToken),
            _ => Task.FromResult(Result(
                item.Action,
                MaintenanceOutcome.Blocked,
                utcNow(),
                affectedFiles: 0,
                affectedBytes: 0,
                "El adaptador de temporales no admite esta acción.",
                "No se realizó ningún cambio.")),
        };
    }

    private async Task<MaintenanceActionResult> InventoryAsync(
        MaintenanceActionDescriptor action,
        CancellationToken cancellationToken)
    {
        await Task.Yield();
        var startedAt = utcNow();
        if (!TryValidateRoot(out var validationFailure))
        {
            inventory = null;
            return Result(
                action,
                MaintenanceOutcome.Blocked,
                startedAt,
                affectedFiles: null,
                affectedBytes: null,
                validationFailure,
                "La carpeta temporal no superó la validación de seguridad.");
        }

        var cutoff = startedAt - minimumAge;
        var candidates = new List<TemporaryFileCandidate>();
        var skippedItems = 0;
        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
                ReturnSpecialDirectories = false,
            };

            foreach (var path in Directory.EnumerateFiles(temporaryRoot, "*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var fullPath = Path.GetFullPath(path);
                    if (!IsDescendant(fullPath, temporaryRoot))
                    {
                        skippedItems++;
                        continue;
                    }

                    var file = new FileInfo(fullPath);
                    if (file.LastWriteTimeUtc <= cutoff.UtcDateTime)
                    {
                        candidates.Add(new TemporaryFileCandidate(
                            fullPath,
                            file.Length,
                            file.LastWriteTimeUtc));
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    skippedItems++;
                }
            }
        }
        catch (OperationCanceledException)
        {
            inventory = null;
            return Result(
                action,
                MaintenanceOutcome.Cancelled,
                startedAt,
                affectedFiles: 0,
                affectedBytes: 0,
                "Inventario cancelado; no se guardó un inventario parcial.",
                "El adaptador no creó archivos temporales.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            inventory = null;
            return Result(
                action,
                MaintenanceOutcome.Failed,
                startedAt,
                affectedFiles: null,
                affectedBytes: null,
                $"No se pudo enumerar la carpeta temporal: {exception.Message}",
                "No se realizó ningún cambio.");
        }

        inventory = new TemporaryFileInventory(
            temporaryRoot,
            startedAt,
            cutoff,
            candidates,
            skippedItems);
        var totalBytes = candidates.Sum(candidate => candidate.Length);
        return Result(
            action,
            MaintenanceOutcome.Succeeded,
            startedAt,
            candidates.Count,
            totalBytes,
            $"Inventario vigente: {candidates.Count} archivos elegibles; {skippedItems} elementos omitidos.",
            "Solo se leyó metadata; el inventario se conserva únicamente en memoria.");
    }

    private async Task<MaintenanceActionResult> CleanupAsync(
        MaintenanceActionDescriptor action,
        CancellationToken cancellationToken)
    {
        await Task.Yield();
        var startedAt = utcNow();
        var currentInventory = inventory;
        if (currentInventory is null ||
            !string.Equals(currentInventory.RootPath, temporaryRoot, StringComparison.OrdinalIgnoreCase) ||
            startedAt - currentInventory.CreatedAt > MaximumInventoryAge)
        {
            return Result(
                action,
                MaintenanceOutcome.Blocked,
                startedAt,
                affectedFiles: 0,
                affectedBytes: 0,
                "No existe un inventario vigente para esta carpeta temporal.",
                "No se realizó ningún cambio.");
        }

        var deletedFiles = 0L;
        var deletedBytes = 0L;
        var changedOrUnavailable = 0;
        try
        {
            foreach (var candidate in currentInventory.Candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (!IsDescendant(candidate.FullPath, temporaryRoot))
                    {
                        changedOrUnavailable++;
                        continue;
                    }

                    var file = new FileInfo(candidate.FullPath);
                    if (!file.Exists)
                    {
                        continue;
                    }

                    if (file.Length != candidate.Length ||
                        file.LastWriteTimeUtc != candidate.LastWriteTimeUtc)
                    {
                        changedOrUnavailable++;
                        continue;
                    }

                    file.Delete();
                    deletedFiles++;
                    deletedBytes += candidate.Length;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    changedOrUnavailable++;
                }
            }
        }
        catch (OperationCanceledException)
        {
            inventory = null;
            return Result(
                action,
                MaintenanceOutcome.Cancelled,
                startedAt,
                deletedFiles,
                deletedBytes,
                "Limpieza cancelada; se detuvo antes del siguiente archivo.",
                "No se usaron archivos de preparación; el adaptador no dejó temporales propios.");
        }

        var remaining = currentInventory.Candidates.Count(candidate => File.Exists(candidate.FullPath));
        inventory = null;
        var outcome = changedOrUnavailable == 0
            ? MaintenanceOutcome.Succeeded
            : MaintenanceOutcome.Failed;
        return Result(
            action,
            outcome,
            startedAt,
            deletedFiles,
            deletedBytes,
            $"Se eliminaron {deletedFiles} archivos; {changedOrUnavailable} cambiaron o no estuvieron disponibles.",
            $"Verificación posterior: {remaining} candidatos inventariados todavía existen; no quedaron temporales propios del adaptador.");
    }

    private bool TryValidateRoot(out string failure)
    {
        if (!Directory.Exists(temporaryRoot))
        {
            failure = "La carpeta temporal no existe.";
            return false;
        }

        if (!Directory.Exists(userProfileRoot) || !IsDescendant(temporaryRoot, userProfileRoot))
        {
            failure = "La carpeta temporal no pertenece al perfil del usuario actual.";
            return false;
        }

        var attributes = File.GetAttributes(temporaryRoot);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            failure = "La raíz temporal es un punto de análisis y no se puede limpiar con seguridad.";
            return false;
        }

        failure = string.Empty;
        return true;
    }

    private static bool IsDescendant(string candidatePath, string rootPath)
    {
        var relative = Path.GetRelativePath(rootPath, candidatePath);
        return relative.Length > 0 &&
            !Path.IsPathFullyQualified(relative) &&
            !relative.Equals("..", StringComparison.Ordinal) &&
            !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private MaintenanceActionResult Result(
        MaintenanceActionDescriptor action,
        MaintenanceOutcome outcome,
        DateTimeOffset startedAt,
        long? affectedFiles,
        long? affectedBytes,
        string detail,
        string verification) => new(
            action.Id,
            action.Version,
            action.Priority,
            action.Risk,
            outcome,
            startedAt,
            utcNow(),
            affectedFiles,
            affectedBytes,
            ExitCode: null,
            RestartRequired: false,
            KnownTemporaryArtifactsRemain: false,
            detail,
            verification);
}
