using WinMaint.Domain;

namespace WinMaint.Application;

public sealed record PcManagerAuthorization(
    bool Approved,
    PcManagerProposedAction ApprovedAction);

public static class PcManagerAuthorizationPolicy
{
    public static string? RequiredToken(PcManagerProposedAction action) => action switch
    {
        PcManagerProposedAction.InstallFromMicrosoftStoreWithWinGet => "pc-manager.install",
        PcManagerProposedAction.OpenOfficialStorePage => "pc-manager.store",
        PcManagerProposedAction.LaunchInstalled => "pc-manager.launch",
        _ => null,
    };

    public static PcManagerAuthorization Evaluate(
        PcManagerProposedAction action,
        string? suppliedToken)
    {
        var requiredToken = RequiredToken(action);
        var approved = requiredToken is not null &&
            string.Equals(requiredToken, suppliedToken, StringComparison.Ordinal);
        return new PcManagerAuthorization(
            approved,
            approved ? action : PcManagerProposedAction.None);
    }
}

public interface IPcManagerActionRunner
{
    Task<PcManagerActionResult> RunAsync(
        PcManagerActionPlan plan,
        CancellationToken cancellationToken);
}

public sealed class PcManagerActionCoordinator(
    IPcManagerActionRunner runner,
    bool installExecutionEnabled,
    bool externalLaunchEnabled)
{
    public const string ResultSchemaVersion = "1.0";

    public async Task<PcManagerActionResult> ExecuteAsync(
        PcManagerActionPlan plan,
        PcManagerAuthorization authorization,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(authorization);
        if (plan.IsBlocked)
        {
            return NotRun(plan, PcManagerActionOutcome.Blocked, plan.Detail);
        }

        if (!authorization.Approved || authorization.ApprovedAction != plan.Action)
        {
            return NotRun(
                plan,
                PcManagerActionOutcome.Cancelled,
                "La acción no recibió una confirmación exacta; no se invocó ningún proceso externo.");
        }

        if (plan.Action == PcManagerProposedAction.InstallFromMicrosoftStoreWithWinGet &&
            !installExecutionEnabled)
        {
            return NotRun(
                plan,
                PcManagerActionOutcome.Blocked,
                "La instalación permanece bloqueada hasta completar la validación controlada de Fase 5.");
        }

        if ((plan.Action is PcManagerProposedAction.LaunchInstalled or
            PcManagerProposedAction.OpenOfficialStorePage) && !externalLaunchEnabled)
        {
            return NotRun(
                plan,
                PcManagerActionOutcome.Blocked,
                "La apertura de aplicaciones externas permanece bloqueada durante la validación.");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await runner.RunAsync(plan, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return NotRun(
                plan,
                PcManagerActionOutcome.Cancelled,
                "Cancelación solicitada; no se reporta la acción como exitosa.");
        }
    }

    private static PcManagerActionResult NotRun(
        PcManagerActionPlan plan,
        PcManagerActionOutcome outcome,
        string detail)
    {
        var now = DateTimeOffset.Now;
        return new PcManagerActionResult(
            ResultSchemaVersion,
            plan.Action,
            outcome,
            now,
            now,
            ExitCode: null,
            detail,
            "No se inició WinGet, Microsoft Store ni Microsoft PC Manager.");
    }
}
