using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Win32;
using WinMaint.Application;
using WinMaint.Domain;

namespace WinMaint.Infrastructure.Win;

internal sealed class PerformanceProvider : IReadOnlyDiagnosticProvider
{
    public string Id => "performance";
    public string Title => "CPU, RAM y procesos";

    public async Task<DiagnosticSection> CollectAsync(CancellationToken cancellationToken)
    {
        var data = new List<DiagnosticDatum>();
        CollectCpuIdentity(data);
        await CollectCpuUsageAsync(data, cancellationToken).ConfigureAwait(false);
        CollectMemory(data);
        CollectTopProcesses(data);
        return DiagnosticSectionFactory.Create(Id, Title, data);
    }

    private static void CollectCpuIdentity(List<DiagnosticDatum> data)
    {
        const string source = "Registro de CPU (solo lectura)";
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0",
                writable: false);
            var processorName = Convert.ToString(key?.GetValue("ProcessorNameString"), CultureInfo.InvariantCulture)?.Trim();
            data.Add(string.IsNullOrWhiteSpace(processorName)
                ? DiagnosticDatum.Unknown("Procesador", source, "El modelo no está disponible.")
                : DiagnosticDatum.Available("Procesador", processorName, source));
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException)
        {
            data.Add(DiagnosticDatum.Unknown("Procesador", source, "No se pudo leer la identidad del procesador."));
        }

        data.Add(DiagnosticDatum.Available(
            "Procesadores lógicos",
            Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture),
            "Environment.ProcessorCount"));
    }

    private static async Task CollectCpuUsageAsync(
        List<DiagnosticDatum> data,
        CancellationToken cancellationToken)
    {
        const string source = "GetSystemTimes (muestra de 350 ms)";
        if (!TryReadSystemTimes(out var first))
        {
            data.Add(DiagnosticDatum.Unknown("Uso de CPU", source, "No se pudo leer la primera muestra."));
            return;
        }

        await Task.Delay(TimeSpan.FromMilliseconds(350), cancellationToken).ConfigureAwait(false);
        if (!TryReadSystemTimes(out var second))
        {
            data.Add(DiagnosticDatum.Unknown("Uso de CPU", source, "No se pudo leer la segunda muestra."));
            return;
        }

        var idleDelta = second.Idle - first.Idle;
        var totalDelta = (second.Kernel - first.Kernel) + (second.User - first.User);
        if (totalDelta == 0 || idleDelta > totalDelta)
        {
            data.Add(DiagnosticDatum.Unknown("Uso de CPU", source, "La muestra no fue válida."));
            return;
        }

        var usage = 100d * (totalDelta - idleDelta) / totalDelta;
        data.Add(DiagnosticDatum.Available(
            "Uso de CPU",
            DiagnosticFormatting.Percentage(usage),
            source,
            EvidenceCoverage.Partial,
            "Muestra instantánea; se reporta como rendimiento, no como salud.",
            "performance.cpu.percent",
            usage));
    }

    private static void CollectMemory(List<DiagnosticDatum> data)
    {
        const string source = "GlobalMemoryStatusEx";
        var memory = MemoryStatusEx.Create();
        if (!NativeMethods.GlobalMemoryStatusEx(ref memory))
        {
            data.Add(DiagnosticDatum.Unknown("Memoria física", source, "La API no devolvió información."));
            return;
        }

        var used = memory.TotalPhysical - memory.AvailablePhysical;
        data.Add(DiagnosticDatum.Available("RAM total", DiagnosticFormatting.Bytes(memory.TotalPhysical), source));
        data.Add(DiagnosticDatum.Available("RAM utilizada", DiagnosticFormatting.Bytes(used), source));
        data.Add(DiagnosticDatum.Available(
            "RAM disponible",
            DiagnosticFormatting.Bytes(memory.AvailablePhysical),
            source,
            metricKey: "memory.available.bytes",
            numericValue: memory.AvailablePhysical));
        data.Add(DiagnosticDatum.Available(
            "Carga de memoria",
            memory.MemoryLoad.ToString(CultureInfo.InvariantCulture) + " %",
            source,
            metricKey: "memory.load.percent",
            numericValue: memory.MemoryLoad));
        data.Add(DiagnosticDatum.Available("Memoria comprometida", DiagnosticFormatting.Bytes(memory.TotalPageFile - memory.AvailablePageFile), source));
        data.Add(DiagnosticDatum.Available("Límite comprometido", DiagnosticFormatting.Bytes(memory.TotalPageFile), source));
    }

    private static void CollectTopProcesses(List<DiagnosticDatum> data)
    {
        const string source = "System.Diagnostics.Process (solo lectura)";
        var samples = new List<ProcessSample>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    samples.Add(new ProcessSample(process.ProcessName, process.Id, (ulong)process.WorkingSet64));
                }
                catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
                {
                    // Algunos procesos protegidos pueden desaparecer o denegar métricas.
                }
            }
        }

        if (samples.Count == 0)
        {
            data.Add(DiagnosticDatum.Unknown("Procesos con mayor memoria", source, "No se obtuvieron procesos accesibles."));
            return;
        }

        var rank = 1;
        foreach (var sample in samples.OrderByDescending(item => item.WorkingSet).Take(5))
        {
            data.Add(DiagnosticDatum.Available(
                $"Proceso #{rank}",
                $"{sample.Name} (PID {sample.Id.ToString(CultureInfo.InvariantCulture)}) — {DiagnosticFormatting.Bytes(sample.WorkingSet)}",
                source,
                EvidenceCoverage.Partial,
                "Ordenado por working set en una muestra puntual."));
            rank++;
        }
    }

    private static bool TryReadSystemTimes(out SystemTimesSnapshot snapshot)
    {
        if (!NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            snapshot = default;
            return false;
        }

        snapshot = new SystemTimesSnapshot(idle.ToUInt64(), kernel.ToUInt64(), user.ToUInt64());
        return true;
    }

    private readonly record struct SystemTimesSnapshot(ulong Idle, ulong Kernel, ulong User);
    private sealed record ProcessSample(string Name, int Id, ulong WorkingSet);
}
