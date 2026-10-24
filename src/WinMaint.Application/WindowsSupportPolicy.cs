using WinMaint.Domain;

namespace WinMaint.Application;

// Context only: platform compatibility and support/licensing are different facts.
public static class WindowsSupportPolicy
{
    public const string Version = "1.0.0";
    private const string Source = "Microsoft Lifecycle / ESU; política WinMaint " + Version;
    private const string Lifecycle = "https://learn.microsoft.com/en-us/lifecycle/products/windows-10-home-and-pro";
    private const string Esu = "https://learn.microsoft.com/en-us/windows/whats-new/extended-security-updates";
    private static readonly DateOnly StandardEnd = new(2025, 10, 14);

    public static IReadOnlyList<DiagnosticDatum> Evaluate(string generation, string? edition,
        string? displayVersion, DateOnly asOf)
    {
        if (generation != "Windows 10")
            return [DiagnosticDatum.Unknown("Soporte del sistema operativo", Source,
                "La compatibilidad técnica de WinMaint no acredita el ciclo de soporte de esta versión/edición.",
                "system.support.status")];

        var generalNotice = asOf > StandardEnd
            ? "Windows 10 Home/Pro: soporte estándar terminado el 14 de octubre de 2025."
            : "Windows 10 Home/Pro: fin general del soporte estándar el 14 de octubre de 2025; versiones anteriores pueden tener otro plazo.";
        var homeOrPro = edition is "Core" or "CoreN" or "CoreSingleLanguage" or "CoreCountrySpecific" or
            "Professional" or "ProfessionalN" or "ProfessionalEducation" or "ProfessionalEducationN" or
            "ProfessionalWorkstation" or "ProfessionalWorkstationN";
        var ltsc = edition is "EnterpriseS" or "EnterpriseSN" or "IoTEnterpriseS" or "IoTEnterpriseSK";
        var knownEnded = homeOrPro && displayVersion == "22H2" && asOf > StandardEnd;
        var detail = ltsc
            ? "Edición LTSC/LTSB: tiene ciclo específico; no se aplica automáticamente el fin general. Verificar la edición y versión exactas."
            : knownEnded ? "El fin del soporte estándar no determina la inscripción ni el estado de ESU de este equipo."
            : "Ciclo exacto no verificado: confirmar versión y edición; LTSC/LTSB tiene ciclos específicos.";
        return
        [
            DiagnosticDatum.Available("Aviso de soporte Windows 10", generalNotice, Source,
                EvidenceCoverage.Partial, Lifecycle, "system.support.notice"),
            knownEnded
                ? DiagnosticDatum.Available("Soporte estándar de esta edición/versión", "Terminado", Source,
                    EvidenceCoverage.Partial, detail + " " + Lifecycle, "system.support.status", textValue: "standard-ended")
                : DiagnosticDatum.Unknown("Soporte de esta edición/versión", Source, detail + " " + Lifecycle,
                    "system.support.status"),
            DiagnosticDatum.Unknown("Inscripción ESU (no comprobada)", Source,
                "No se consultó la inscripción ni la licencia ESU; no se presume protección ni falta de ella. " + Esu,
                "system.esu.enrollment"),
        ];
    }
}
