using WinMaint.Domain;

namespace WinMaint.Application;

public sealed record DemoSystemSummary(string Label, string Value, string Origin);

public sealed record DemoFinding(
    Priority Priority,
    string Finding,
    string Recommendation,
    RiskLevel Risk,
    string Origin);

public sealed record DemoDashboardSnapshot(
    string Banner,
    string HealthStatus,
    string ConfidenceStatus,
    IReadOnlyList<DemoSystemSummary> SystemSummary,
    IReadOnlyList<DemoFinding> Findings,
    bool IsSimulated);

public sealed record MenuOption(string Id, string Label, bool IsImplemented);

public sealed record DemoMaintenanceChoice(
    string Id,
    string Label,
    RiskLevel Risk,
    bool IsSimulated,
    bool IsExecutable);
