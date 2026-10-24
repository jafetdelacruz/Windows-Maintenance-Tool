namespace WinMaint.Domain;

public enum PreflightStatus
{
    Pass,
    Warning,
    Block,
    Unknown,
}

public enum StabilityProbeStatus
{
    Passed,
    Failed,
    Cancelled,
    TimedOut,
    Skipped,
}

public sealed record PreflightCheck(
    string Id,
    string Label,
    PreflightStatus Status,
    string Detail);

public sealed record StabilityPreflight(
    IReadOnlyList<PreflightCheck> Checks,
    bool CanRun)
{
    public bool HasBlock => Checks.Any(check => check.Status == PreflightStatus.Block);
}

public sealed record StabilityProbeResult(
    string ProbeId,
    string Label,
    StabilityProbeStatus Status,
    TimeSpan Duration,
    string Detail,
    IReadOnlyDictionary<string, double> Metrics);

public sealed record StabilityTestResult(
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    StabilityPreflight Preflight,
    IReadOnlyList<StabilityProbeResult> Probes,
    bool WasInterrupted,
    bool WroteBenchmarkData,
    IReadOnlyList<DiagnosticFinding> Findings)
{
    public TimeSpan Duration => FinishedAt - StartedAt;
}
