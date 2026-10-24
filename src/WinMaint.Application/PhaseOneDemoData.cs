using WinMaint.Domain;

namespace WinMaint.Application;

public static class PhaseFiveUiData
{
    public const string SimulatedOrigin = "SIMULADO — FASE 1";
    public const string PendingOrigin = "NO RECOPILADO";

    public static DemoDashboardSnapshot CreateDashboard() => new(
        Banner: "FASE 8 — INVENTARIO R0 | ELIMINACIÓN BLOQUEADA | REPORTES LOCALES",
        HealthStatus: "Se calcula después del diagnóstico",
        ConfidenceStatus: "Pendiente de ejecutar el diagnóstico",
        SystemSummary:
        [
            new("Modo", "Solo lectura (R0)", "CONFIGURACIÓN"),
            new("Sistema", "Selecciona Diagnóstico completo", PendingOrigin),
            new("RAM", "Selecciona Diagnóstico completo", PendingOrigin),
            new("Almacenamiento", "Selecciona Diagnóstico completo", PendingOrigin),
        ],
        Findings: [],
        IsSimulated: false);

    public static IReadOnlyList<MenuOption> CreateMainMenu() =>
    [
        new("diagnostic", "1. Diagnóstico completo (solo lectura)", true),
        new("maintenance", "2. Mantenimiento recomendado", true),
        new("memory-storage", "3. RAM y almacenamiento", false),
        new("stability", "4. Prueba rápida de estabilidad", true),
        new("cleanup", "5. Limpieza", true),
        new("integrity", "6. Integridad de Windows", true),
        new("startup", "7. Inicio y procesos", false),
        new("pc-manager", "8. Microsoft PC Manager", true),
        new("reports", "9. Reportes", true),
        new("exit", "10. Salir", true),
    ];

    public static IReadOnlyList<MaintenanceActionDescriptor> CreateMaintenanceChoices() =>
        MaintenanceCatalog.SafeActions;
}
