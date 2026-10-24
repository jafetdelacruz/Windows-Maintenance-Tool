using System.Globalization;
using WinMaint.Application;
using WinMaint.Domain;

namespace WinMaint.Infrastructure.Win;

internal sealed class StorageProvider : IReadOnlyDiagnosticProvider
{
    private const string Script = """
        $ErrorActionPreference = 'Stop'
        $items = @(Get-PhysicalDisk | ForEach-Object {
            $disk = $_
            $reliability = $null
            try { $reliability = $disk | Get-StorageReliabilityCounter -ErrorAction Stop } catch { }
            [pscustomobject]@{
                FriendlyName = [string]$disk.FriendlyName
                MediaType = [string]$disk.MediaType
                BusType = [string]$disk.BusType
                HealthStatus = [string]$disk.HealthStatus
                OperationalStatus = [string]($disk.OperationalStatus -join ', ')
                Size = [uint64]$disk.Size
                Temperature = if ($null -eq $reliability.Temperature) { $null } else { [int]$reliability.Temperature }
                Wear = if ($null -eq $reliability.Wear) { $null } else { [int]$reliability.Wear }
                PowerOnHours = if ($null -eq $reliability.PowerOnHours) { $null } else { [long]$reliability.PowerOnHours }
                ReadErrorsTotal = if ($null -eq $reliability.ReadErrorsTotal) { $null } else { [long]$reliability.ReadErrorsTotal }
                WriteErrorsTotal = if ($null -eq $reliability.WriteErrorsTotal) { $null } else { [long]$reliability.WriteErrorsTotal }
                ReliabilityAvailable = ($null -ne $reliability)
            }
        })
        ConvertTo-Json -InputObject $items -Compress -Depth 4
        """;

    public string Id => "storage";
    public string Title => "Almacenamiento y volúmenes";

    public async Task<DiagnosticSection> CollectAsync(CancellationToken cancellationToken)
    {
        var data = new List<DiagnosticDatum>();
        var notes = new List<string>();
        CollectVolumes(data, notes);
        await CollectPhysicalDisksAsync(data, notes, cancellationToken).ConfigureAwait(false);
        return DiagnosticSectionFactory.Create(Id, Title, data, [.. notes]);
    }

    private static void CollectVolumes(List<DiagnosticDatum> data, List<string> notes)
    {
        const string source = "System.IO.DriveInfo (solo lectura)";
        try
        {
            var fixedDrives = DriveInfo.GetDrives().Where(drive => drive.DriveType == DriveType.Fixed).ToArray();
            if (fixedDrives.Length == 0)
            {
                data.Add(DiagnosticDatum.Unknown("Volúmenes", source, "No se encontraron unidades fijas."));
                return;
            }

            foreach (var drive in fixedDrives)
            {
                if (!drive.IsReady)
                {
                    data.Add(DiagnosticDatum.Unknown($"Volumen {drive.Name}", source, "El volumen no está listo.", "storage.volume.free.percent"));
                    continue;
                }

                var total = (ulong)drive.TotalSize;
                var free = (ulong)drive.AvailableFreeSpace;
                var freePercent = total == 0 ? 0d : 100d * free / total;
                data.Add(DiagnosticDatum.Available(
                    $"Volumen {drive.Name}",
                    $"{drive.DriveFormat}; {DiagnosticFormatting.Bytes(free)} libres de {DiagnosticFormatting.Bytes(total)}",
                    source,
                    metricKey: "storage.volume.free.percent",
                    numericValue: freePercent));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            data.Add(DiagnosticDatum.Unknown("Volúmenes", source, "No se pudo enumerar el almacenamiento lógico.", "storage.volume.free.percent"));
            notes.Add("La enumeración de volúmenes produjo un error controlado.");
        }
    }

    private static async Task CollectPhysicalDisksAsync(
        List<DiagnosticDatum> data,
        List<string> notes,
        CancellationToken cancellationToken)
    {
        const string source = "Storage Management Provider (solo lectura)";
        var result = await PowerShellJsonRunner.RunAsync(Script, TimeSpan.FromSeconds(45), cancellationToken).ConfigureAwait(false);
        if (result.TimedOut)
        {
            data.Add(DiagnosticDatum.Unknown("Discos físicos", source, "La consulta agotó el tiempo de espera."));
            return;
        }

        if (result.ExitCode != 0)
        {
            data.Add(DiagnosticDatum.Unknown(
                "Discos físicos",
                source,
                $"PowerShell terminó con código {result.ExitCode.ToString(CultureInfo.InvariantCulture)}."));
            return;
        }

        IReadOnlyList<PhysicalDiskInfo> disks;
        try
        {
            disks = CimJsonParser.ParsePhysicalDisks(result.StandardOutput);
        }
        catch (System.Text.Json.JsonException)
        {
            data.Add(DiagnosticDatum.Unknown("Discos físicos", source, "La salida no tuvo el formato esperado."));
            return;
        }

        if (disks.Count == 0)
        {
            data.Add(DiagnosticDatum.Unknown("Discos físicos", source, "Windows no devolvió discos físicos."));
            return;
        }

        for (var index = 0; index < disks.Count; index++)
        {
            AddDisk(data, disks[index], index, source);
        }

        if (disks.Any(disk => !disk.ReliabilityAvailable))
        {
            notes.Add("Uno o más dispositivos no expusieron contadores de fiabilidad; no se interpretan como sanos.");
        }
    }

    internal static void AddDisk(
        List<DiagnosticDatum> data,
        PhysicalDiskInfo disk,
        int index,
        string source)
    {
        var name = string.IsNullOrWhiteSpace(disk.FriendlyName) ? $"Disco {index + 1}" : disk.FriendlyName.Trim();
        var size = disk.Size is null ? "tamaño desconocido" : DiagnosticFormatting.Bytes(disk.Size.Value);
        var media = string.IsNullOrWhiteSpace(disk.MediaType) ? "tipo desconocido" : disk.MediaType;
        var bus = string.IsNullOrWhiteSpace(disk.BusType) ? "bus desconocido" : disk.BusType;
        data.Add(DiagnosticDatum.Available(name, $"{media}; {bus}; {size}", source, EvidenceCoverage.Partial));

        data.Add(string.IsNullOrWhiteSpace(disk.HealthStatus)
            ? DiagnosticDatum.Unknown($"{name} — HealthStatus", source, "Estado no reportado.", "storage.disk.health")
            : DiagnosticDatum.Available(
                $"{name} — HealthStatus",
                disk.HealthStatus,
                source,
                metricKey: "storage.disk.health",
                textValue: disk.HealthStatus));
        var states = DiskOperationalPolicy.Split(disk.OperationalStatus);
        if (states.Count == 0)
            data.Add(DiagnosticDatum.Unknown($"{name} — estado operativo", source, "Estado no reportado.", "storage.disk.operational"));
        foreach (var state in states)
            data.Add(DiagnosticDatum.Available($"{name} — estado operativo", state, source,
                DiskOperationalPolicy.Classify(state) == DiskOperationalState.Unknown ? EvidenceCoverage.Absent : EvidenceCoverage.Complete,
                detail: DiskOperationalPolicy.Classify(state) == DiskOperationalState.Unknown ? "Estado no normal confirmado; requiere interpretación." : null,
                metricKey: "storage.disk.operational", textValue: state));

        AddCounter(data, name, "temperatura", disk.Temperature, " °C", source, "storage.disk.temperature.celsius");
        AddCounter(data, name, "desgaste", disk.Wear, " %", source, "storage.disk.wear.percent");
        AddCounter(data, name, "horas encendido", disk.PowerOnHours, " h", source, "storage.disk.poweron.hours");
        AddCounter(data, name, "errores de lectura", disk.ReadErrorsTotal, string.Empty, source, "storage.disk.read-errors");
        AddCounter(data, name, "errores de escritura", disk.WriteErrorsTotal, string.Empty, source, "storage.disk.write-errors");
    }

    private static void AddCounter<T>(
        List<DiagnosticDatum> data,
        string diskName,
        string counterName,
        T? value,
        string suffix,
        string source,
        string metricKey)
        where T : struct, IFormattable
    {
        data.Add(value is null
            ? DiagnosticDatum.Unknown($"{diskName} — {counterName}", source, "Contador no disponible.", metricKey)
            : DiagnosticDatum.Available(
                $"{diskName} — {counterName}",
                value.Value.ToString(null, CultureInfo.InvariantCulture) + suffix,
                source,
                metricKey: metricKey,
                numericValue: Convert.ToDouble(value.Value, CultureInfo.InvariantCulture)));
    }
}
