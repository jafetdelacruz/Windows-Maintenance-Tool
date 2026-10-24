namespace WinMaint.Infrastructure.Win;

/// <summary>
/// Declara que la Fase 1 no contiene adaptadores, comandos ni mutaciones reales.
/// </summary>
public static class PhaseOneWindowsBoundary
{
    public const bool HasOperatingSystemAdapters = false;
    public const bool HasExternalProcessExecution = false;
    public const bool HasMutableActions = false;
}

/// <summary>Límite de seguridad de los adaptadores implementados en la Fase 2.</summary>
public static class PhaseTwoWindowsBoundary
{
    public const bool HasOperatingSystemAdapters = true;
    public const bool HasReadOnlyExternalProcessExecution = true;
    public const bool HasMutableActions = false;
}
