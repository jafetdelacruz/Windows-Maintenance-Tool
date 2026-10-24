using System.Globalization;
using WinMaint.Application;
using WinMaint.Domain;

namespace WinMaint.Infrastructure.Win;

internal sealed class MemoryModulesProvider : IReadOnlyDiagnosticProvider
{
    private const string Script = """
        $ErrorActionPreference = 'Stop'
        $items = @(Get-CimInstance -ClassName Win32_PhysicalMemory | ForEach-Object {
            [pscustomobject]@{
                BankLabel = [string]$_.BankLabel
                DeviceLocator = [string]$_.DeviceLocator
                Manufacturer = [string]$_.Manufacturer
                Capacity = [uint64]$_.Capacity
                Speed = if ($null -eq $_.Speed) { $null } else { [uint32]$_.Speed }
                ConfiguredClockSpeed = if ($null -eq $_.ConfiguredClockSpeed) { $null } else { [uint32]$_.ConfiguredClockSpeed }
            }
        })
        ConvertTo-Json -InputObject $items -Compress -Depth 4
        """;

    public string Id => "memory-modules";
    public string Title => "Módulos de RAM";

    public async Task<DiagnosticSection> CollectAsync(CancellationToken cancellationToken)
    {
        const string source = "CIM Win32_PhysicalMemory (solo lectura)";
        var result = await PowerShellJsonRunner.RunAsync(Script, TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        if (result.TimedOut)
        {
            return UnknownSection(source, "La consulta CIM agotó el tiempo de espera.");
        }

        if (result.ExitCode != 0)
        {
            return UnknownSection(source, $"PowerShell terminó con código {result.ExitCode.ToString(CultureInfo.InvariantCulture)}.");
        }

        IReadOnlyList<MemoryModuleInfo> modules;
        try
        {
            modules = CimJsonParser.ParseMemoryModules(result.StandardOutput);
        }
        catch (System.Text.Json.JsonException)
        {
            return UnknownSection(source, "La salida CIM no tuvo el formato esperado.");
        }

        if (modules.Count == 0)
        {
            return UnknownSection(source, "Windows no devolvió módulos de memoria.");
        }

        var data = new List<DiagnosticDatum>();
        for (var index = 0; index < modules.Count; index++)
        {
            var module = modules[index];
            var location = FirstAvailable(module.DeviceLocator, module.BankLabel, $"Módulo {index + 1}");
            var capacity = module.Capacity is null
                ? DiagnosticDatum.Unknown($"{location} — capacidad", source, "Capacidad no reportada.")
                : DiagnosticDatum.Available($"{location} — capacidad", DiagnosticFormatting.Bytes(module.Capacity.Value), source);
            data.Add(capacity);

            data.Add(string.IsNullOrWhiteSpace(module.Manufacturer)
                ? DiagnosticDatum.Unknown($"{location} — fabricante", source, "Fabricante no reportado.")
                : DiagnosticDatum.Available($"{location} — fabricante", module.Manufacturer.Trim(), source));

            var speed = module.ConfiguredClockSpeed ?? module.Speed;
            data.Add(speed is null or 0
                ? DiagnosticDatum.Unknown($"{location} — velocidad", source, "Velocidad no reportada.")
                : DiagnosticDatum.Available(
                    $"{location} — velocidad",
                    speed.Value.ToString(CultureInfo.InvariantCulture) + " MT/s",
                    source,
                    module.ConfiguredClockSpeed is null ? EvidenceCoverage.Partial : EvidenceCoverage.Complete,
                    module.ConfiguredClockSpeed is null ? "Se muestra la velocidad nominal." : null));
        }

        return DiagnosticSectionFactory.Create(Id, Title, data);
    }

    private DiagnosticSection UnknownSection(string source, string reason) =>
        DiagnosticSectionFactory.Create(
            Id,
            Title,
            [DiagnosticDatum.Unknown("Módulos de RAM", source, reason)],
            reason);

    private static string FirstAvailable(params string?[] values) =>
        values.First(value => !string.IsNullOrWhiteSpace(value))!;
}
