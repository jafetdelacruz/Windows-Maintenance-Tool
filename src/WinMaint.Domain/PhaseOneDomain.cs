namespace WinMaint.Domain;

/// <summary>Urgencia de un hallazgo. No autoriza una acción.</summary>
public enum Priority
{
    P0,
    P1,
    P2,
    P3,
}

/// <summary>Riesgo de una acción propuesta, separado de su prioridad.</summary>
public enum RiskLevel
{
    R0,
    R1,
    R2,
    R3,
}

/// <summary>Límite explícito del esqueleto entregado en la Fase 1.</summary>
public static class PhaseOneCapabilities
{
    public const bool HasRealDiagnostics = false;
    public const bool HasMutableActions = false;
}
