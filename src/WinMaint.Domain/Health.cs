namespace WinMaint.Domain;

public enum HealthDomain
{
    HardwareReliability,
    Storage,
    Memory,
    WindowsIntegrity,
    LoadStability,
}

public sealed record DiagnosticFinding(
    string RuleId,
    string RuleVersion,
    HealthDomain Domain,
    Priority Priority,
    RiskLevel SuggestedActionRisk,
    string Summary,
    string Recommendation,
    string Evidence);

public sealed record HealthDomainScore(
    HealthDomain Domain,
    int? Score,
    int ConfidencePercent,
    bool HasSufficientEvidence);

public sealed record HealthAssessment(
    string RuleSetVersion,
    int? OverallScore,
    int ConfidencePercent,
    string Band,
    IReadOnlyList<HealthDomainScore> Domains,
    IReadOnlyList<DiagnosticFinding> Findings)
{
    public bool HasPriorityZero => Findings.Any(finding => finding.Priority == Priority.P0);
    public MemoryEvidenceAssessment? MemoryEvidence { get; init; }
}

public static class PhaseThreeCapabilities
{
    public const bool HasVersionedHealthRules = true;
    public const bool HasControlledStabilityTest = true;
    public const bool HasMaintenanceActions = false;
}
