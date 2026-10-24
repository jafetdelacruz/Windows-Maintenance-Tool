namespace WinMaint.Domain;

public enum AvailabilityState
{
    Available,
    Unavailable,
    Unknown,
}

public enum PcManagerInstallationState
{
    Installed,
    Absent,
    Unknown,
}

public enum StorePolicyState
{
    NotBlockedByKnownPolicy,
    Blocked,
    Unknown,
}

public sealed record PcManagerPackageInfo(
    string Name,
    string Version,
    string PackageFamilyName,
    string? ApplicationUserModelId);

public sealed record PcManagerDetectionResult(
    string SchemaVersion,
    DateTimeOffset CapturedAt,
    int OperatingSystemBuild,
    bool IsX64OperatingSystem,
    AvailabilityState SystemCompatibility,
    PcManagerInstallationState Installation,
    PcManagerPackageInfo? Package,
    AvailabilityState WinGet,
    AvailabilityState MicrosoftStorePackage,
    StorePolicyState MicrosoftStorePolicy,
    AvailabilityState NetworkInterface,
    IReadOnlyList<string> Evidence);

public enum PcManagerProposedAction
{
    None,
    LaunchInstalled,
    InstallFromMicrosoftStoreWithWinGet,
    OpenOfficialStorePage,
}

public sealed record PcManagerActionPlan(
    string SchemaVersion,
    DateTimeOffset CreatedAt,
    PcManagerProposedAction Action,
    Priority Priority,
    RiskLevel Risk,
    bool IsMutable,
    bool RequiresExplicitAuthorization,
    bool RequiresNetwork,
    MaintenanceResource Resources,
    string Detail)
{
    public bool IsBlocked => Action == PcManagerProposedAction.None;
}

public enum PcManagerActionOutcome
{
    Succeeded,
    Failed,
    Cancelled,
    Blocked,
}

public sealed record PcManagerActionResult(
    string SchemaVersion,
    PcManagerProposedAction Action,
    PcManagerActionOutcome Outcome,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    int? ExitCode,
    string Detail,
    string Verification);

public static class PhaseFiveCapabilities
{
    public const bool HasReadOnlyDetection = true;
    public const bool HasInstallationEnabled = false;
    public const bool HasStoreFallbackEnabled = true;
    public const bool HasLaunchEnabled = true;
}
