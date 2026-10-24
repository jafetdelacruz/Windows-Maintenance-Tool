using WinMaint.Domain;

namespace WinMaint.Application;

public static class MaintenanceCatalog
{
    public const string CatalogVersion = "1.0.0";

    public static IReadOnlyList<MaintenanceActionDescriptor> SafeActions { get; } =
    [
        Action(
            "temp.inventory", "Inventariar temporales", "Calcula archivos y bytes elegibles antes de borrar.",
            "Limpieza", Priority.P3, RiskLevel.R0, false, false, false, false,
            ReversalKind.NotNeeded, "Solo lectura; no requiere reversión.", MaintenanceResource.Storage,
            [Required("temp-path", "La carpeta temporal del usuario debe resolverse dentro de su perfil.")]),
        Action(
            "temp.cleanup", "Limpiar temporales elegibles", "Elimina solo elementos inventariados, antiguos y no bloqueados.",
            "Limpieza", Priority.P3, RiskLevel.R2, true, false, false, false,
            ReversalKind.Irreversible, "Los elementos eliminados no pasan por la papelera.", MaintenanceResource.Storage,
            [Required("inventory-current", "Debe existir un inventario vigente con tamaño exacto."), Required("age-policy", "Debe aplicarse la antigüedad mínima configurada.")],
            ["temp.inventory"]),
        Action(
            "recycle.inventory", "Inventariar papelera", "Calcula por separado el contenido de la papelera del usuario.",
            "Papelera", Priority.P3, RiskLevel.R0, false, false, false, false,
            ReversalKind.NotNeeded, "Solo lectura; no requiere reversión.", MaintenanceResource.Storage,
            [Required("recycle-access", "La papelera debe poder consultarse para el usuario actual.")]),
        Action(
            "recycle.empty", "Vaciar papelera", "Elimina de forma permanente el contenido confirmado de la papelera.",
            "Papelera", Priority.P3, RiskLevel.R2, true, false, false, false,
            ReversalKind.Irreversible, "Windows no ofrece reversión después de vaciar la papelera.", MaintenanceResource.Storage,
            [Required("recycle-inventory-current", "Debe existir un inventario vigente y separado de la papelera.")],
            ["recycle.inventory"]),
        Action(
            "volume.analyze", "Analizar optimización de unidades", "Solicita a Windows el análisis antes de optimizar.",
            "Almacenamiento", Priority.P3, RiskLevel.R0, false, true, false, true,
            ReversalKind.NotNeeded, "Solo lectura; no requiere reversión.", MaintenanceResource.Storage,
            [Required("volume-supported", "La unidad debe ser local, fija y compatible."), Required("volume-not-dirty", "La unidad no debe estar marcada como dañada.")]),
        Action(
            "volume.optimize", "Optimizar según el tipo de unidad", "Delega en Windows la operación apropiada para el medio; no fuerza desfragmentación.",
            "Almacenamiento", Priority.P3, RiskLevel.R2, true, true, false, true,
            ReversalKind.Manual, "No existe reversión general; se verifica el volumen al terminar.", MaintenanceResource.Storage,
            [Required("analysis-current", "Windows debe haber analizado antes la unidad."), Required("volume-not-dirty", "La unidad no debe estar marcada como dañada.")],
            ["volume.analyze"]),
        Action(
            "chkdsk.scan", "Examinar sistema de archivos", "Ejecuta el examen en línea de NTFS sin parámetros de reparación.",
            "Integridad", Priority.P2, RiskLevel.R1, false, true, false, true,
            ReversalKind.NotNeeded, "No usa /f, /r, /x ni /b.", MaintenanceResource.Storage,
            [Required("ntfs-volume", "La unidad debe usar NTFS."), Required("administrator", "Se requiere una consola elevada.")]),
        Action(
            "dism.scan-health", "Examinar imagen de Windows", "Detecta corrupción reparable sin iniciar reparación.",
            "Integridad", Priority.P2, RiskLevel.R1, false, true, false, true,
            ReversalKind.NotNeeded, "La comprobación no solicita reparación.", MaintenanceResource.Cpu | MaintenanceResource.Storage,
            [Required("administrator", "Se requiere una consola elevada."), Required("windows-online", "Debe estar disponible la imagen activa de Windows.")]),
        Action(
            "dism.restore-health", "Reparar imagen de Windows", "Repara el almacén de componentes antes de ejecutar SFC.",
            "Integridad", Priority.P1, RiskLevel.R2, true, true, true, true,
            ReversalKind.Manual, "Conservar el reporte; una recuperación posterior depende de Windows.", MaintenanceResource.Cpu | MaintenanceResource.Storage | MaintenanceResource.Network,
            [Required("corruption-repairable", "DISM debe haber detectado corrupción reparable."), Required("administrator", "Se requiere una consola elevada."), Required("repair-source", "Windows Update o una fuente de reparación válida debe estar disponible.")],
            ["dism.scan-health"],
            mayRequireRestart: true),
        Action(
            "sfc.scan-now", "Reparar archivos protegidos", "Ejecuta SFC después de validar o reparar el almacén de componentes.",
            "Integridad", Priority.P1, RiskLevel.R2, true, true, false, true,
            ReversalKind.Manual, "Conservar el reporte y usar las opciones de recuperación de Windows si fuera necesario.", MaintenanceResource.Cpu | MaintenanceResource.Storage,
            [Required("component-store-ready", "DISM debe confirmar que el almacén de componentes está listo."), Required("administrator", "Se requiere una consola elevada.")],
            ["dism.restore-health"],
            mayRequireRestart: true),
    ];

    private static MaintenanceActionDescriptor Action(
        string id,
        string label,
        string description,
        string category,
        Priority priority,
        RiskLevel risk,
        bool mutable,
        bool administrator,
        bool network,
        bool exclusive,
        ReversalKind reversal,
        string reversalDescription,
        MaintenanceResource resources,
        IReadOnlyList<MaintenancePrecondition> preconditions,
        IReadOnlyList<string>? dependencies = null,
        bool mayRequireRestart = false) => new(
            id,
            CatalogVersion,
            label,
            description,
            category,
            MaintenanceProfile.Safe,
            priority,
            risk,
            mutable,
            administrator,
            network,
            MayRequireRestart: mayRequireRestart,
            RequiresExclusiveExecution: exclusive,
            SupportsPreview: true,
            reversal,
            reversalDescription,
            resources,
            preconditions,
            dependencies ?? []);

    private static MaintenancePrecondition Required(string id, string description) =>
        new(id, description, IsRequired: true);
}
