using Microsoft.Win32;
using WinMaint.Application;
using WinMaint.Domain;

namespace WinMaint.Infrastructure.Win;

internal sealed class RestartAndIntegrityProvider : IReadOnlyDiagnosticProvider
{
    private static readonly string[] RebootKeys =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending",
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired",
    ];

    public string Id => "restart-integrity";
    public string Title => "Reinicio pendiente e integridad";

    public Task<DiagnosticSection> CollectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var data = new List<DiagnosticDatum>();
        CollectRestartStatus(data);
        CollectIntegrityAvailability(data);
        return Task.FromResult(DiagnosticSectionFactory.Create(
            Id,
            Title,
            data,
            "Fase 2 no ejecuta DISM ni SFC: incluso las comprobaciones pueden escribir logs del sistema."));
    }

    private static void CollectRestartStatus(List<DiagnosticDatum> data)
    {
        const string source = "Registro de Windows (solo lectura)";
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            var signals = new List<string>();
            foreach (var path in RebootKeys)
            {
                using var key = baseKey.OpenSubKey(path, writable: false);
                if (key is not null)
                {
                    signals.Add(path.Split('\\')[^1]);
                }
            }

            using var sessionManager = baseKey.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Session Manager",
                writable: false);
            if (sessionManager?.GetValue("PendingFileRenameOperations") is string[] pendingRenames
                && pendingRenames.Length > 0)
            {
                signals.Add("PendingFileRenameOperations");
            }

            data.Add(DiagnosticDatum.Available(
                "Reinicio pendiente",
                signals.Count == 0 ? "Sin señales detectadas" : "Detectado",
                source,
                EvidenceCoverage.Partial,
                signals.Count == 0
                    ? "Se consultaron señales conocidas; Windows no ofrece una única fuente exhaustiva."
                    : "Señales: " + string.Join(", ", signals),
                "system.reboot.pending",
                textValue: signals.Count == 0 ? "false" : "true"));
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException)
        {
            data.Add(DiagnosticDatum.Unknown("Reinicio pendiente", source, "No se pudieron consultar todas las claves."));
        }
    }

    private static void CollectIntegrityAvailability(List<DiagnosticDatum> data)
    {
        var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var dism = Path.Combine(systemDirectory, "dism.exe");
        var sfc = Path.Combine(systemDirectory, "sfc.exe");
        data.Add(DiagnosticDatum.Available(
            "Herramientas de integridad",
            File.Exists(dism) && File.Exists(sfc) ? "DISM y SFC disponibles" : "Disponibilidad parcial",
            "Sistema de archivos (solo lectura)",
            File.Exists(dism) && File.Exists(sfc) ? EvidenceCoverage.Complete : EvidenceCoverage.Partial));
        data.Add(DiagnosticDatum.Unknown(
            "Estado de integridad",
            "No ejecutado",
            "La comprobación explícita corresponde al flujo controlado de una fase posterior.",
            "windows.integrity.status"));
    }
}
