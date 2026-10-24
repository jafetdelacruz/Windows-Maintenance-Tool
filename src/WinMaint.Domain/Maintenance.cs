namespace WinMaint.Domain;

public enum MaintenanceProfile
{
    Safe,
    Advanced,
    Experimental,
}

[Flags]
public enum MaintenanceResource
{
    None = 0,
    Cpu = 1,
    Memory = 2,
    Storage = 4,
    Network = 8,
    Store = 16,
}

public enum ReversalKind
{
    NotNeeded,
    Automatic,
    Manual,
    Irreversible,
}

public enum MaintenancePreconditionStatus
{
    Pass,
    Warning,
    Block,
    Unknown,
}

public sealed record MaintenancePrecondition(
    string Id,
    string Description,
    bool IsRequired);

public sealed record MaintenanceActionDescriptor(
    string Id,
    string Version,
    string Label,
    string Description,
    string Category,
    MaintenanceProfile MinimumProfile,
    Priority Priority,
    RiskLevel Risk,
    bool IsMutable,
    bool RequiresAdministrator,
    bool RequiresNetwork,
    bool MayRequireRestart,
    bool RequiresExclusiveExecution,
    bool SupportsPreview,
    ReversalKind Reversal,
    string ReversalDescription,
    MaintenanceResource Resources,
    IReadOnlyList<MaintenancePrecondition> Preconditions,
    IReadOnlyList<string> DependsOn);

public sealed record MaintenancePreconditionResult(
    string Id,
    MaintenancePreconditionStatus Status,
    string Detail);

public sealed record MaintenancePlanItem(
    MaintenanceActionDescriptor Action,
    IReadOnlyList<MaintenancePreconditionResult> Preconditions)
{
    public bool IsBlocked => Preconditions.Any(item =>
            item.Status == MaintenancePreconditionStatus.Block) ||
        Action.Preconditions
            .Where(precondition => precondition.IsRequired)
            .Any(precondition => Preconditions.All(result =>
                result.Id != precondition.Id ||
                result.Status != MaintenancePreconditionStatus.Pass));
}

public sealed record MaintenancePlan(
    string SchemaVersion,
    DateTimeOffset CreatedAt,
    MaintenanceProfile Profile,
    IReadOnlyList<MaintenancePlanItem> Items)
{
    public bool HasMutableActions => Items.Any(item => item.Action.IsMutable);
    public bool IsBlocked => Items.Any(item => item.IsBlocked);
}

public enum MaintenanceOutcome
{
    Succeeded,
    Failed,
    Cancelled,
    Blocked,
    Skipped,
}

public sealed record MaintenanceActionResult(
    string ActionId,
    string ActionVersion,
    Priority Priority,
    RiskLevel Risk,
    MaintenanceOutcome Outcome,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    long? AffectedFiles,
    long? AffectedBytes,
    int? ExitCode,
    bool RestartRequired,
    bool KnownTemporaryArtifactsRemain,
    string Detail,
    string PostActionVerification);

public sealed record MaintenanceReport(
    string SchemaVersion,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    IReadOnlyList<MaintenanceActionResult> Actions)
{
    public bool KnownTemporaryArtifactsRemain =>
        Actions.Any(action => action.KnownTemporaryArtifactsRemain);
}

public static class PhaseFourCapabilities
{
    public const bool HasVersionedActionCatalog = true;
    public const bool HasExecutablePreview = true;
    public const bool HasMutableExecutionEnabled = false;
    public const bool HasAggressiveOptimizations = false;
}
