using System.Text;
using System.Text.Json;
using Spectre.Console;
using WinMaint.Application;
using WinMaint.Domain;
using WinMaint.Infrastructure.Win;
using WinMaint.Reporting;

namespace WinMaint.Cli;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            return await RunAsync(args).ConfigureAwait(false);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            Console.Error.WriteLine($"No se pudo completar la operación ({exception.GetType().Name}). Revisa el archivo, la ruta o los permisos. Se conservan los reportes existentes.");
            return 4;
        }
    }

    private static async Task<int> RunAsync(string[] args)
    {
        var options = CliOptions.Parse(args);
        var plainText = options.NoAnsi || Console.IsOutputRedirected;
        var console = ConsoleFactory.Create(plainText);

        if (options.ShowHelp)
        {
            console.Write(new Text(ProductIdentity.DisplayVersion + Environment.NewLine));
            console.Write(new Text("Uso: WinMaint.exe [--no-ansi] [--smoke-test | --diagnostic | --quick-test --confirm-work-saved | --pc-manager-detect | --pc-manager-store | --report | --report-summary ARCHIVO.json] [--report-dir CARPETA] [--help]" + Environment.NewLine));
            return 0;
        }

        if (!LaunchPolicy.SupportsPlatform(OperatingSystem.IsWindows(),
            System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.X64,
            Environment.OSVersion.Version.Build))
        {
            console.Write(new Text("Esta candidata requiere Windows 10 22H2 o Windows 11 x64." + Environment.NewLine));
            return 3;
        }

        if (options.RunPhaseFourLabTemp)
        {
#if PHASE4_LAB
            return await PhaseFourLabShell.RunTemporaryCleanupAsync(console).ConfigureAwait(false);
#else
            console.Write(new Text("El laboratorio mutable no está incluido en esta candidata." + Environment.NewLine));
            return 3;
#endif
        }

        if (options.SummaryReportPath is not null)
        {
            var sourcePath = Path.GetFullPath(options.SummaryReportPath);
            var report = ReportRenderer.Parse(await File.ReadAllTextAsync(sourcePath).ConfigureAwait(false));
            var outputPath = Path.ChangeExtension(sourcePath, ".summary.txt");
            if (File.Exists(outputPath))
            {
                throw new IOException("Ya existe un resumen con ese nombre; no se sobrescribe.");
            }

            await using var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await using var writer = new StreamWriter(output, new UTF8Encoding(true));
            await writer.WriteAsync(ReportRenderer.ToText(report)).ConfigureAwait(false);
            console.Write(new Text(outputPath + Environment.NewLine));
            return 0;
        }

        if (options.RunPhaseFivePc2Lab)
        {
            console.MarkupLine("[red]El laboratorio de instalación automática fue retirado. Usa la ficha oficial de Microsoft Store.[/]");
            return 2;
        }

        var sessionStore = new FileStabilitySessionStore();
        using var cleanupJournal = new FileTemporaryCleanupJournal(FileTemporaryCleanupJournal.DefaultDirectory);
        var shell = new WinMaintShell(
            console,
            plainText,
            WindowsDiagnosticComposition.Create(),
            WindowsStabilityComposition.Create(sessionStore),
            sessionStore,
            WindowsPcManagerComposition.CreateDetector(),
            WindowsPcManagerComposition.CreateActionCoordinator(
                installExecutionEnabled: false,
                PhaseFiveCapabilities.HasStoreFallbackEnabled && PhaseFiveCapabilities.HasLaunchEnabled),
            new ReportJournal(options.ReportDirectory ?? ReportJournal.DefaultDirectory),
            new WindowsPreparedTemporaryInventoryProvider(), cleanupJournal);
        return await shell.RunAsync(
            options.SmokeTest,
            options.RunDiagnostic,
            options.RunStability,
            options.ConfirmWorkSaved,
            options.RunPcManagerDetection,
            options.RunPcManagerStore,
            options.RunReport).ConfigureAwait(false);
    }
}
