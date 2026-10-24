using WinMaint.Domain;

namespace WinMaint.Application;

public static class PcManagerPlanBuilder
{
    public const string SchemaVersion = "1.0";

    public static PcManagerActionPlan Build(PcManagerDetectionResult detection)
    {
        ArgumentNullException.ThrowIfNull(detection);
        if (detection.SystemCompatibility != AvailabilityState.Available)
        {
            return Blocked("El sistema no cumple Windows 10 build 19042+ o Windows 11 x64.");
        }

        if (detection.Installation == PcManagerInstallationState.Unknown)
        {
            return Blocked("El estado de instalación es desconocido; no se propondrá ninguna acción.");
        }

        if (detection.Installation == PcManagerInstallationState.Installed)
        {
            return new PcManagerActionPlan(
                SchemaVersion,
                DateTimeOffset.Now,
                PcManagerProposedAction.LaunchInstalled,
                Priority.P3,
                RiskLevel.R0,
                IsMutable: false,
                RequiresExplicitAuthorization: true,
                RequiresNetwork: false,
                MaintenanceResource.None,
                "PC Manager está instalado; se puede ofrecer abrirlo sin automatizar controles internos.");
        }

        return BuildStorePage(detection);
    }

    public static PcManagerActionPlan BuildStorePage(PcManagerDetectionResult detection)
    {
        ArgumentNullException.ThrowIfNull(detection);
        if (detection.SystemCompatibility != AvailabilityState.Available)
        {
            return Blocked("El sistema no cumple Windows 10 build 19042+ o Windows 11 x64.");
        }

        if (detection.MicrosoftStorePolicy == StorePolicyState.Blocked)
        {
            return Blocked("Una política conocida bloquea el acceso al catálogo público de Microsoft Store.");
        }

        if (detection.MicrosoftStorePolicy == StorePolicyState.Unknown)
        {
            return Blocked("No se pudo determinar si una política bloquea Microsoft Store.");
        }

        if (detection.NetworkInterface != AvailabilityState.Available)
        {
            return Blocked(detection.NetworkInterface == AvailabilityState.Unavailable
                ? "No hay una interfaz de red disponible para la instalación."
                : "La disponibilidad de red es desconocida.");
        }

        if (detection.MicrosoftStorePackage == AvailabilityState.Available)
        {
            return new PcManagerActionPlan(
                SchemaVersion,
                DateTimeOffset.Now,
                PcManagerProposedAction.OpenOfficialStorePage,
                Priority.P3,
                RiskLevel.R0,
                IsMutable: false,
                RequiresExplicitAuthorization: true,
                RequiresNetwork: true,
                MaintenanceResource.Network | MaintenanceResource.Store,
                "Abrir la ficha oficial de Microsoft Store. La instalación se realiza manualmente desde Store.");
        }

        return Blocked("El paquete de Microsoft Store no está disponible o su estado es desconocido.");
    }

    private static PcManagerActionPlan Blocked(string detail) => new(
        SchemaVersion,
        DateTimeOffset.Now,
        PcManagerProposedAction.None,
        Priority.P3,
        RiskLevel.R0,
        IsMutable: false,
        RequiresExplicitAuthorization: false,
        RequiresNetwork: false,
        MaintenanceResource.None,
        detail);
}
