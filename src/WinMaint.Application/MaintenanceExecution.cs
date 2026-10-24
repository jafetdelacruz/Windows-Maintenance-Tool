using WinMaint.Domain;

namespace WinMaint.Application;

public sealed record MaintenanceAuthorization(
    bool Approved,
    IReadOnlySet<string> ApprovedActionIds);

public interface IMaintenanceActionRunner
{
    Task<MaintenanceActionResult> RunAsync(
        MaintenancePlanItem item,
        CancellationToken cancellationToken);
}

public sealed class MaintenanceCoordinator(
    IMaintenanceActionRunner runner,
    bool mutableExecutionEnabled)
{
    public const string ReportSchemaVersion = "1.0";

    public async Task<MaintenanceReport> ExecuteAsync(
        MaintenancePlan plan,
        MaintenanceAuthorization authorization,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(authorization);
        var startedAt = DateTimeOffset.Now;
        var results = new List<MaintenanceActionResult>();

        foreach (var item in plan.Items)
        {
            if (item.IsBlocked)
            {
                results.Add(NotRun(item.Action, MaintenanceOutcome.Blocked, "Una precondición bloqueó la acción."));
                continue;
            }

            if (item.Action.IsMutable && (!mutableExecutionEnabled ||
                !authorization.Approved ||
                !authorization.ApprovedActionIds.Contains(item.Action.Id)))
            {
                var reason = !mutableExecutionEnabled
                    ? "Esta compilación no habilita la ejecución mutable de producción."
                    : "La acción mutable no recibió confirmación exacta.";
                results.Add(NotRun(item.Action, MaintenanceOutcome.Blocked, reason));
                continue;
            }

            try
            {
                var result = await runner.RunAsync(item, cancellationToken).ConfigureAwait(false);
                results.Add(result);
                if (result.Outcome == MaintenanceOutcome.Cancelled)
                {
                    AddSkippedResults(plan, results);
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                results.Add(NotRun(item.Action, MaintenanceOutcome.Cancelled, "Cancelación solicitada; se ejecutará la limpieza del adaptador."));
                AddSkippedResults(plan, results);
                break;
            }
        }

        return new MaintenanceReport(ReportSchemaVersion, startedAt, DateTimeOffset.Now, results);
    }

    private static void AddSkippedResults(
        MaintenancePlan plan,
        List<MaintenanceActionResult> results)
    {
        foreach (var remaining in plan.Items.Skip(results.Count))
        {
            results.Add(NotRun(remaining.Action, MaintenanceOutcome.Skipped, "Omitida después de la cancelación."));
        }
    }

    private static MaintenanceActionResult NotRun(
        MaintenanceActionDescriptor action,
        MaintenanceOutcome outcome,
        string detail)
    {
        var now = DateTimeOffset.Now;
        return new MaintenanceActionResult(
            action.Id,
            action.Version,
            action.Priority,
            action.Risk,
            outcome,
            now,
            now,
            AffectedFiles: 0,
            AffectedBytes: 0,
            ExitCode: null,
            RestartRequired: false,
            KnownTemporaryArtifactsRemain: false,
            detail,
            "No se ejecutó ninguna acción; no aplica verificación posterior.");
    }
}
