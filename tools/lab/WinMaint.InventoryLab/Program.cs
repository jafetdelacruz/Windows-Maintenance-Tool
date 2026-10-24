using System.Text;
using System.Text.Json;
using WinMaint.Application;
using WinMaint.Domain;
using WinMaint.Infrastructure.Win;

namespace WinMaint.InventoryLab;

internal static class Program
{
    private const string Marker = @"C:\WinMaint-Phase8-Inventory.marker";
    private static readonly List<LabCheck> Checks = [];
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var executable = Environment.ProcessPath;
        if (args.Length != 0 || !OperatingSystem.IsWindows()
            || !Environment.UserName.Equals("WDAGUtilityAccount", StringComparison.Ordinal)
            || executable is null || !IsUnder(executable, @"C:\WinMaintPhase8"))
        {
            Console.WriteLine("BLOCKED: run this inventory-only lab inside the supplied Windows Sandbox profile.");
            return 3;
        }

        if (File.Exists(Marker))
        {
            Console.WriteLine("RECREATION CHECK FAILED: close Sandbox completely and open the profile again.");
            return 4;
        }

        Console.WriteLine("RECREATION CHECK PASSED: no marker from a previous lab session.");
        var basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Temp", "WinMaintPhase8InventoryLab", Guid.NewGuid().ToString("N"));
        var profile = Path.Combine(basePath, "profile");
        var root = Path.Combine(profile, "AppData", "Local", "Temp");
        Directory.CreateDirectory(root);
        var now = DateTimeOffset.UtcNow;
        var old = MakeFile(root, "old.bin", "OLD-KEEP", now.AddDays(-10));
        var recent = MakeFile(root, "recent.bin", "RECENT-KEEP", now);
        var outside = Path.Combine(basePath, "outside");
        Directory.CreateDirectory(outside);
        var sentinel = MakeFile(outside, "sentinel.bin", "OUTSIDE-KEEP", now.AddDays(-10));
        try
        {
            var provider = new WindowsTemporaryInventoryProvider(profile, root, () => now);
            var baseline = await provider.CollectAsync(CancellationToken.None).ConfigureAwait(false);
            Check("old-only-baseline", Number(baseline, "temp.preview.files.count") == 1
                && Number(baseline, "temp.preview.bytes") == Encoding.UTF8.GetByteCount("OLD-KEEP"));

            Directory.CreateSymbolicLink(Path.Combine(root, "outside-directory-link"), outside);
            File.CreateSymbolicLink(Path.Combine(root, "outside-file-link.bin"), sentinel);
            var linked = await provider.CollectAsync(CancellationToken.None).ConfigureAwait(false);
            Check("file-and-directory-links-excluded", Number(linked, "temp.preview.files.count") == 1
                && Number(linked, "temp.preview.protected.count") == 2);

            var other = await new WindowsTemporaryInventoryProvider(profile, outside)
                .CollectAsync(CancellationToken.None).ConfigureAwait(false);
            Check("foreign-root-blocked", other.Coverage == EvidenceCoverage.Absent);

            var ancestorProfile = Path.Combine(basePath, "ancestor-profile");
            Directory.CreateDirectory(ancestorProfile);
            var outsideAppData = Path.Combine(basePath, "outside-appdata");
            Directory.CreateDirectory(Path.Combine(outsideAppData, "Local", "Temp"));
            Directory.CreateSymbolicLink(Path.Combine(ancestorProfile, "AppData"), outsideAppData);
            var ancestor = await new WindowsTemporaryInventoryProvider(ancestorProfile,
                Path.Combine(ancestorProfile, "AppData", "Local", "Temp"))
                .CollectAsync(CancellationToken.None).ConfigureAwait(false);
            Check("linked-ancestor-blocked", ancestor.Coverage == EvidenceCoverage.Absent);

            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var cancelled = false;
            try
            {
                await provider.CollectAsync(cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }

            Check("cancelled-inventory-preserves-files", cancelled
                && File.ReadAllText(old) == "OLD-KEEP" && File.ReadAllText(recent) == "RECENT-KEEP");

            var preserved = Path.Combine(basePath, "preserved-temp");
            if (!IsUnder(root, basePath) || !IsUnder(preserved, basePath))
            {
                throw new InvalidOperationException("Fixture paths escaped the owned lab directory.");
            }

            // Solo cambia la carpeta ficticia de esta VM; no elimina sus archivos.
            Directory.Move(root, preserved);
            Directory.CreateSymbolicLink(root, outside);
            var replaced = await provider.CollectAsync(CancellationToken.None).ConfigureAwait(false);
            Check("replaced-root-blocked", replaced.Coverage == EvidenceCoverage.Absent);
            Check("all-fixture-contents-preserved", File.ReadAllText(sentinel) == "OUTSIDE-KEEP"
                && File.ReadAllText(Path.Combine(preserved, "old.bin")) == "OLD-KEEP"
                && File.ReadAllText(Path.Combine(preserved, "recent.bin")) == "RECENT-KEEP");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        {
            Check("lab-operation-" + exception.GetType().Name, false);
        }

        var passed = Checks.Count == 7 && Checks.All(check => check.Passed);
        var resultPath = Path.Combine(basePath, "RESULTADO.json");
        await File.WriteAllTextAsync(resultPath,
            JsonSerializer.Serialize(new { Scope = "InventoryOnly", Checks, Passed = passed }, JsonOptions)).ConfigureAwait(false);
        await using (var marker = new FileStream(Marker, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await marker.WriteAsync("PHASE8_INVENTORY_LAB_SESSION"u8.ToArray()).ConfigureAwait(false);
        }

        Console.ForegroundColor = passed ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine(passed ? "INVENTORY SAFETY GATE PASSED: 7/7. NO FILES DELETED."
            : "INVENTORY SAFETY GATE FAILED. CLEANUP REMAINS BLOCKED.");
        Console.ResetColor();
        Console.WriteLine("RESULT: " + resultPath);
        Console.WriteLine("Close Sandbox completely before running the profile again.");
        return passed ? 0 : 4;
    }

    private static string MakeFile(string folder, string name, string content, DateTimeOffset timestamp)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        File.SetCreationTimeUtc(path, timestamp.UtcDateTime);
        File.SetLastWriteTimeUtc(path, timestamp.UtcDateTime);
        return path;
    }

    private static void Check(string name, bool passed)
    {
        Checks.Add(new(name, passed));
        Console.WriteLine((passed ? "PASS: " : "FAIL: ") + name);
    }

    private static double? Number(DiagnosticSection section, string key) =>
        section.Data.FirstOrDefault(datum => datum.MetricKey == key)?.NumericValue;

    private static bool IsUnder(string path, string root)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return relative.Length > 0 && relative != "." && relative != ".." && !Path.IsPathFullyQualified(relative)
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private sealed record LabCheck(string Name, bool Passed);
}
