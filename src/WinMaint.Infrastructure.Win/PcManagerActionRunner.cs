using System.Diagnostics;
using WinMaint.Application;
using WinMaint.Domain;

namespace WinMaint.Infrastructure.Win;

internal interface IPcManagerPlatformGateway
{
    string? ResolveWinGetPath();

    Task<ControlledProcessResult> RunProcessAsync(
        MaintenanceCommand command,
        TimeSpan timeout,
        CancellationToken cancellationToken);

    bool OpenStoreProductPage(Uri uri);

    bool LaunchApplication(string applicationUserModelId);
}

internal sealed class WindowsPcManagerPlatformGateway : IPcManagerPlatformGateway
{
    public string? ResolveWinGetPath()
    {
        var candidate = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft",
            "WindowsApps",
            "winget.exe");
        return File.Exists(candidate) ? candidate : null;
    }

    public Task<ControlledProcessResult> RunProcessAsync(
        MaintenanceCommand command,
        TimeSpan timeout,
        CancellationToken cancellationToken) =>
        ControlledProcessRunner.RunAsync(
            command.ExecutablePath,
            command.Arguments,
            timeout,
            cancellationToken);

    public bool OpenStoreProductPage(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        Process.Start(new ProcessStartInfo
        {
            FileName = uri.AbsoluteUri,
            UseShellExecute = true,
        });
        return true;
    }

    public bool LaunchApplication(string applicationUserModelId)
    {
        var explorerPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "explorer.exe");
        var startInfo = new ProcessStartInfo
        {
            FileName = explorerPath,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add($"shell:AppsFolder\\{applicationUserModelId}");
        Process.Start(startInfo);
        return true;
    }
}

internal sealed class WindowsPcManagerActionRunner(
    IPcManagerDetector detector,
    IPcManagerPlatformGateway gateway) : IPcManagerActionRunner
{
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(30);

    public Task<PcManagerActionResult> RunAsync(
        PcManagerActionPlan plan,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return plan.Action switch
        {
            PcManagerProposedAction.InstallFromMicrosoftStoreWithWinGet =>
                InstallAsync(plan, cancellationToken),
            PcManagerProposedAction.OpenOfficialStorePage =>
                Task.FromResult(OpenStorePage(plan, cancellationToken)),
            PcManagerProposedAction.LaunchInstalled =>
                LaunchAsync(plan, cancellationToken),
            _ => Task.FromResult(Result(
                plan,
                PcManagerActionOutcome.Blocked,
                DateTimeOffset.Now,
                null,
                "El plan no contiene una acción ejecutable.",
                "No se inició ningún proceso externo.")),
        };
    }

    private async Task<PcManagerActionResult> InstallAsync(
        PcManagerActionPlan plan,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.Now;
        var wingetPath = gateway.ResolveWinGetPath();
        if (wingetPath is null)
        {
            return Result(
                plan,
                PcManagerActionOutcome.Failed,
                startedAt,
                null,
                "WinGet dejó de estar disponible antes de iniciar la instalación.",
                "No se inició ningún proceso externo.");
        }

        var command = PcManagerCommandFactory.InstallWithWinGet(wingetPath);
        var process = await gateway.RunProcessAsync(
            command,
            InstallTimeout,
            cancellationToken).ConfigureAwait(false);
        if (process.TimedOut || process.ExitCode != 0)
        {
            return Result(
                plan,
                PcManagerActionOutcome.Failed,
                startedAt,
                process.ExitCode,
                process.TimedOut
                    ? "La instalación superó el tiempo máximo."
                    : "WinGet devolvió un código distinto de cero.",
                "PC Manager no se considera instalado sin una detección posterior correcta.");
        }

        var verification = await detector.DetectAsync(cancellationToken).ConfigureAwait(false);
        return verification.Installation == PcManagerInstallationState.Installed
            ? Result(
                plan,
                PcManagerActionOutcome.Succeeded,
                startedAt,
                process.ExitCode,
                "WinGet terminó correctamente mediante la fuente msstore.",
                $"Detección posterior aprobada: versión {verification.Package?.Version ?? "desconocida"}.")
            : Result(
                plan,
                PcManagerActionOutcome.Failed,
                startedAt,
                process.ExitCode,
                "WinGet terminó, pero PC Manager no apareció en la detección posterior.",
                "El resultado ausente o desconocido no se convirtió en éxito.");
    }

    private PcManagerActionResult OpenStorePage(PcManagerActionPlan plan, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.Now;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var opened = gateway.OpenStoreProductPage(PcManagerCommandFactory.StoreProductPage());
            return Result(
                plan,
                opened ? PcManagerActionOutcome.Succeeded : PcManagerActionOutcome.Failed,
                startedAt,
                null,
                opened ? "Se solicitó abrir la ficha oficial de Microsoft Store." : "Windows rechazó la solicitud de apertura.",
                opened ? "La ficha usa el ProductId oficial; la instalación queda bajo control del usuario." : "No se abrió Microsoft Store.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return Result(
                plan,
                PcManagerActionOutcome.Failed,
                startedAt,
                null,
                "No se pudo abrir Microsoft Store.",
                $"Windows devolvió {exception.GetType().Name}; no se usó una fuente alternativa.");
        }
    }

    private async Task<PcManagerActionResult> LaunchAsync(
        PcManagerActionPlan plan,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.Now;
        var detection = await detector.DetectAsync(cancellationToken).ConfigureAwait(false);
        var applicationUserModelId = detection.Package?.ApplicationUserModelId;
        if (detection.Installation != PcManagerInstallationState.Installed ||
            !IsValidApplicationUserModelId(applicationUserModelId))
        {
            return Result(
                plan,
                PcManagerActionOutcome.Failed,
                startedAt,
                null,
                "No se encontró una identidad de aplicación válida para PC Manager.",
                "No se inició ninguna aplicación.");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var launched = gateway.LaunchApplication(applicationUserModelId!);
            return Result(
                plan,
                launched ? PcManagerActionOutcome.Succeeded : PcManagerActionOutcome.Failed,
                startedAt,
                null,
                launched ? "Windows recibió la solicitud de abrir PC Manager." : "Windows rechazó la solicitud de apertura.",
                "WinMaint no automatizó controles internos de PC Manager.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return Result(
                plan,
                PcManagerActionOutcome.Failed,
                startedAt,
                null,
                "No se pudo abrir PC Manager.",
                $"Windows devolvió {exception.GetType().Name}.");
        }
    }

    private static bool IsValidApplicationUserModelId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256 || value.Count(character => character == '!') != 1)
        {
            return false;
        }

        return value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-' or '!');
    }

    private static PcManagerActionResult Result(
        PcManagerActionPlan plan,
        PcManagerActionOutcome outcome,
        DateTimeOffset startedAt,
        int? exitCode,
        string detail,
        string verification) => new(
            PcManagerActionCoordinator.ResultSchemaVersion,
            plan.Action,
            outcome,
            startedAt,
            DateTimeOffset.Now,
            exitCode,
            detail,
            verification);
}
