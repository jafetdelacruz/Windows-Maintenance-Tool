using System.Globalization;
using Spectre.Console;
using WinMaint.Application;
using WinMaint.Domain;
using WinMaint.Infrastructure.Win;

namespace WinMaint.Cli;

internal static class PhaseFourLabShell
{
    public static async Task<int> RunTemporaryCleanupAsync(IAnsiConsole console)
    {
        ArgumentNullException.ThrowIfNull(console);
        WriteLine(console, "Hecho por Jafet");
        WriteLine(console, "WinMaint — laboratorio descartable de Fase 4");
        WriteLine(console, string.Empty);

        if (!WindowsMaintenanceComposition.IsPhaseFourSandboxLab())
        {
            WriteLine(console, "BLOQUEADO: este modo solo funciona desde el perfil WinMaint de Windows Sandbox.");
            return 3;
        }

        var runner = WindowsMaintenanceComposition.CreatePhaseFourTemporaryLabRunner();
        var inventoryItem = ExecutableItem("temp.inventory");
        var inventory = await runner.RunAsync(inventoryItem, CancellationToken.None).ConfigureAwait(false);
        WriteResult(console, inventory);
        if (inventory.Outcome != MaintenanceOutcome.Succeeded)
        {
            return 4;
        }

        WriteLine(console, string.Empty);
        WriteLine(console, "Solo se eliminará el archivo de prueba antiguo inventariado.");
        WriteLine(console, "Escribe exactamente temp.cleanup para autorizar; Enter cancela sin cambios:");
        var confirmation = Console.ReadLine();
        if (!string.Equals(confirmation, "temp.cleanup", StringComparison.Ordinal))
        {
            WriteLine(console, "CANCELADO: 0 cambios; el adaptador no dejó temporales propios.");
            return 2;
        }

        var cleanupItem = ExecutableItem("temp.cleanup");
        var plan = new MaintenancePlan(
            MaintenancePlanBuilder.SchemaVersion,
            DateTimeOffset.Now,
            MaintenanceProfile.Safe,
            [cleanupItem]);
        var coordinator = new MaintenanceCoordinator(runner, mutableExecutionEnabled: true);
        var report = await coordinator.ExecuteAsync(
            plan,
            new MaintenanceAuthorization(
                Approved: true,
                new HashSet<string>(["temp.cleanup"], StringComparer.Ordinal)),
            CancellationToken.None).ConfigureAwait(false);
        var result = report.Actions.Single();
        WriteResult(console, result);
        return result.Outcome == MaintenanceOutcome.Succeeded ? 0 : 4;
    }

    private static MaintenancePlanItem ExecutableItem(string actionId)
    {
        var action = MaintenanceCatalog.SafeActions.Single(item => item.Id == actionId);
        var preconditions = action.Preconditions.Select(precondition =>
            new MaintenancePreconditionResult(
                precondition.Id,
                MaintenancePreconditionStatus.Pass,
                "Comprobada por el adaptador aislado de laboratorio.")).ToArray();
        return new MaintenancePlanItem(action, preconditions);
    }

    private static void WriteResult(IAnsiConsole console, MaintenanceActionResult result)
    {
        WriteLine(console, $"{result.ActionId}: {result.Outcome}");
        var affectedFiles = result.AffectedFiles?.ToString(CultureInfo.InvariantCulture) ?? "desconocido";
        var affectedBytes = result.AffectedBytes?.ToString(CultureInfo.InvariantCulture) ?? "desconocido";
        WriteLine(console, $"Archivos: {affectedFiles}; bytes: {affectedBytes}");
        WriteLine(console, result.Detail);
        WriteLine(console, result.PostActionVerification);
    }

    private static void WriteLine(IAnsiConsole console, string value) =>
        console.Write(new Text(value + Environment.NewLine));
}
