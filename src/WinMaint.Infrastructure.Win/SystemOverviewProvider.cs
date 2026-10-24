using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using WinMaint.Application;
using WinMaint.Domain;

namespace WinMaint.Infrastructure.Win;

internal sealed class SystemOverviewProvider : IReadOnlyDiagnosticProvider
{
    public string Id => "system";
    public string Title => "Sistema operativo y energía";

    public Task<DiagnosticSection> CollectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var data = new List<DiagnosticDatum>();
        CollectWindowsVersion(data);
        CollectUptime(data);
        CollectPower(data);
        data.Add(DiagnosticDatum.Available(
            "Arquitectura",
            RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),
            "RuntimeInformation"));

        return Task.FromResult(DiagnosticSectionFactory.Create(Id, Title, data));
    }

    private static void CollectWindowsVersion(List<DiagnosticDatum> data)
    {
        const string source = "Registro de Windows (solo lectura)";
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", writable: false);
            if (key is null)
            {
                data.Add(DiagnosticDatum.Unknown("Sistema operativo", source, "La clave CurrentVersion no está disponible."));
                return;
            }

            var buildText = Convert.ToString(key.GetValue("CurrentBuildNumber"), CultureInfo.InvariantCulture);
            var revisionText = Convert.ToString(key.GetValue("UBR"), CultureInfo.InvariantCulture);
            var displayVersion = Convert.ToString(key.GetValue("DisplayVersion"), CultureInfo.InvariantCulture);
            var edition = Convert.ToString(key.GetValue("EditionID"), CultureInfo.InvariantCulture);

            if (!int.TryParse(buildText, NumberStyles.None, CultureInfo.InvariantCulture, out var build))
            {
                data.Add(DiagnosticDatum.Unknown("Sistema operativo", source, "No se pudo interpretar el número de build."));
                return;
            }

            var generation = WindowsVersionClassifier.Classify(build);
            var version = string.IsNullOrWhiteSpace(displayVersion) ? "versión desconocida" : displayVersion;
            var editionLabel = string.IsNullOrWhiteSpace(edition) ? string.Empty : $" {edition}";
            data.Add(DiagnosticDatum.Available("Sistema operativo", $"{generation}{editionLabel}, {version}", source));
            data.Add(DiagnosticDatum.Available(
                "Build",
                string.IsNullOrWhiteSpace(revisionText) ? buildText : $"{buildText}.{revisionText}",
                source,
                string.IsNullOrWhiteSpace(revisionText) ? EvidenceCoverage.Partial : EvidenceCoverage.Complete,
                string.IsNullOrWhiteSpace(revisionText) ? "Revisión UBR no disponible." : null));
            data.Add(DiagnosticDatum.Available(
                "Compatibilidad WinMaint",
                generation is "Windows 10" or "Windows 11" ? "Dentro del alcance" : "Fuera del alcance",
                "Regla de plataforma v1",
                metricKey: "system.compatibility",
                textValue: generation is "Windows 10" or "Windows 11" ? "supported" : "unsupported"));
            data.AddRange(WindowsSupportPolicy.Evaluate(generation, edition, displayVersion,
                DateOnly.FromDateTime(DateTime.UtcNow)));
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            data.Add(DiagnosticDatum.Unknown("Sistema operativo", source, "No se pudo leer CurrentVersion."));
        }
    }

    private static void CollectUptime(List<DiagnosticDatum> data)
    {
        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        var bootTime = DateTimeOffset.Now - uptime;
        data.Add(DiagnosticDatum.Available("Tiempo activo", DiagnosticFormatting.Duration(uptime), "Environment.TickCount64"));
        data.Add(DiagnosticDatum.Available(
            "Último arranque (estimado)",
            bootTime.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture),
            "Environment.TickCount64",
            EvidenceCoverage.Partial,
            "Estimado a partir del contador monotónico del sistema."));
    }

    private static void CollectPower(List<DiagnosticDatum> data)
    {
        const string source = "GetSystemPowerStatus";
        if (!NativeMethods.GetSystemPowerStatus(out var status))
        {
            data.Add(DiagnosticDatum.Unknown("Fuente de energía", source, "La API no devolvió información."));
            return;
        }

        var power = status.AcLineStatus switch
        {
            0 => "Batería",
            1 => "Corriente alterna",
            _ => "Desconocido",
        };
        data.Add(power == DiagnosticDatum.UnknownValue
            ? DiagnosticDatum.Unknown("Fuente de energía", source, "Windows no informó el origen de energía.", "power.source")
            : DiagnosticDatum.Available("Fuente de energía", power, source, metricKey: "power.source", textValue: power));

        data.Add(status.BatteryLifePercent == byte.MaxValue
            ? DiagnosticDatum.Unknown("Carga de batería", source, "El equipo no reporta porcentaje de batería.", "power.battery.percent")
            : DiagnosticDatum.Available(
                "Carga de batería",
                status.BatteryLifePercent.ToString(CultureInfo.InvariantCulture) + " %",
                source,
                metricKey: "power.battery.percent",
                numericValue: status.BatteryLifePercent));
    }
}

internal static class WindowsVersionClassifier
{
    internal static string Classify(int build) => build switch
    {
        >= 22000 => "Windows 11",
        >= 10240 => "Windows 10",
        _ => "Windows no compatible",
    };
}
