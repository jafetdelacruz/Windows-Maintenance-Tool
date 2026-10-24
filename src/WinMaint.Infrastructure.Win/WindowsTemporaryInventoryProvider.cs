using System.Diagnostics;
using System.Globalization;
using WinMaint.Application;
using WinMaint.Domain;

namespace WinMaint.Infrastructure.Win;

public static class WindowsTemporaryInventoryComposition
{
    public static IReadOnlyDiagnosticProvider Create() => new WindowsTemporaryInventoryProvider(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"));
}

// Solo metadata. No comparte ejecutor ni candidatos con el adaptador mutable de laboratorio.
internal sealed class WindowsTemporaryInventoryProvider(
    string profileRoot,
    string temporaryRoot,
    Func<DateTimeOffset>? utcNow = null,
    int maximumEntries = 20_000) : IReadOnlyDiagnosticProvider
{
    internal static readonly TimeSpan MinimumAge = TimeSpan.FromDays(7);
    private static readonly TimeSpan MaximumDuration = TimeSpan.FromSeconds(15);
    private const string Source = "Inventario de metadata temporal (solo lectura)";

    public string Id => "temporary-inventory";
    public string Title => "Temporales — inventario sin eliminación";

    public async Task<DiagnosticSection> CollectAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        var profile = Path.GetFullPath(profileRoot);
        var root = Path.GetFullPath(temporaryRoot);
        var expected = Path.Combine(profile, "AppData", "Local", "Temp");
        if (!root.Equals(expected, StringComparison.OrdinalIgnoreCase)
            || !Directory.Exists(root) || !SafeAncestors(root, profile))
        {
            return DiagnosticSectionFactory.Create(Id, Title,
                [DiagnosticDatum.Unknown("Inventario", Source,
                    "La raíz no es AppData\\Local\\Temp del perfil actual, no existe o contiene enlaces.")],
                "Inventario bloqueado. No se modificó ningún archivo.");
        }

        var cutoff = (utcNow?.Invoke() ?? DateTimeOffset.UtcNow) - MinimumAge;
        var clock = Stopwatch.StartNew();
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((root, 0));
        long eligible = 0;
        long bytes = 0;
        long recent = 0;
        long protectedItems = 0;
        long unavailable = 0;
        var visited = 0;
        var limited = false;
        while (pending.TryPop(out var folder))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (clock.Elapsed >= MaximumDuration || visited >= maximumEntries)
            {
                limited = true;
                break;
            }

            if (!SafeAncestors(folder.Path, profile))
            {
                unavailable++;
                continue;
            }

            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(folder.Path))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (clock.Elapsed >= MaximumDuration || visited >= maximumEntries)
                    {
                        limited = true;
                        break;
                    }

                    visited++;
                    try
                    {
                        var attributes = File.GetAttributes(entry);
                        if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.System | FileAttributes.Offline)) != 0)
                        {
                            protectedItems++;
                            continue;
                        }

                        if ((attributes & FileAttributes.Directory) != 0)
                        {
                            if (folder.Depth >= 32 || IsProtectedFolder(Path.GetFileName(entry)))
                            {
                                protectedItems++;
                                limited |= folder.Depth >= 32;
                            }
                            else
                            {
                                pending.Push((entry, folder.Depth + 1));
                            }

                            continue;
                        }

                        if ((attributes & FileAttributes.ReadOnly) != 0)
                        {
                            protectedItems++;
                            continue;
                        }

                        var file = new FileInfo(entry);
                        if (file.CreationTimeUtc > cutoff.UtcDateTime || file.LastWriteTimeUtc > cutoff.UtcDateTime)
                        {
                            recent++;
                            continue;
                        }

                        eligible++;
                        bytes += file.Length;
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        unavailable++;
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                unavailable++;
            }

            if (limited)
            {
                break;
            }
        }

        var partial = limited || unavailable > 0;
        var coverage = partial ? EvidenceCoverage.Partial : EvidenceCoverage.Complete;
        return DiagnosticSectionFactory.Create(Id, Title,
        [
            DiagnosticDatum.Available("Alcance", "%LOCALAPPDATA%\\Temp del usuario actual", Source),
            DiagnosticDatum.Available("Estado del recorrido", partial ? "Parcial" : "Terminado", Source, coverage),
            Count("Candidatos por antigüedad", "temp.preview.files.count", eligible, coverage),
            Count("Bytes lógicos candidatos", "temp.preview.bytes", bytes, coverage),
            Count("Archivos recientes excluidos", "temp.preview.recent.count", recent, coverage),
            Count("Elementos protegidos excluidos", "temp.preview.protected.count", protectedItems, coverage),
            Count("Elementos no accesibles", "temp.preview.unavailable.count", unavailable, coverage),
        ],
        "Antigüedad mínima interna: 7 días desde creación y última escritura. No se abrieron contenidos ni se borraron archivos.",
        "Candidatos no significa bytes liberables: uso, bloqueos y cambios deberán revalidarse antes de cualquier eliminación.",
        partial ? "Recorrido incompleto: los conteos solo describen lo observado." : "Recorrido terminado dentro del alcance permitido.");
    }

    internal static bool IsProtectedFolder(string name) =>
        name.Equals(".net", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("WinMaint", StringComparison.OrdinalIgnoreCase);

    private static bool SafeAncestors(string path, string profile)
    {
        var current = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(profile, current);
        if (Path.IsPathFullyQualified(relative) || relative == ".."
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            while (true)
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    return false;
                }

                if (current.Equals(profile, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                var parent = Path.GetDirectoryName(current);
                if (parent is null)
                {
                    return false;
                }

                current = parent;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static DiagnosticDatum Count(string name, string key, long value, EvidenceCoverage coverage) =>
        DiagnosticDatum.Available(name, value.ToString(CultureInfo.InvariantCulture), Source,
            coverage, metricKey: key, numericValue: value);
}
