namespace WinMaint.Domain;

public enum MemoryPhysicalState { InsufficientEvidence, NoFaultsObservedInPreviousTest, PreviousFailureRequiresReview }

public sealed record MemoryEvidenceAssessment(double? CurrentLoadPercent, bool? HighUseInCurrentSample,
    EvidenceCoverage SustainedPressureCoverage, MemoryPhysicalState PhysicalState,
    int PhysicalCoveragePercent, string PreviousDiagnosticStatus, string ProcessPatternStatus)
{
    public string Summary =>
        "RAM: " + (HighUseInCurrentSample is true ? "uso alto en esta muestra" :
            HighUseInCurrentSample is false ? "uso no alto en esta muestra" : "uso actual desconocido") +
        "; presión sostenida no medida; " + (PhysicalState switch
        {
            MemoryPhysicalState.PreviousFailureRequiresReview => "fallo previo de memoria pendiente de revisión.",
            MemoryPhysicalState.NoFaultsObservedInPreviousTest => "sin señales de fallo en la prueba previa, no es certificación física.",
            _ => "diagnóstico físico insuficiente.",
        });
}
