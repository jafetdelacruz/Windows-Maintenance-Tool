namespace WinMaint.Application;

public sealed record CliOptions(
    bool NoAnsi,
    bool SmokeTest,
    bool RunDiagnostic,
    bool RunStability,
    bool ConfirmWorkSaved,
    bool RunPhaseFourLabTemp,
    bool RunPcManagerDetection,
    bool RunPcManagerStore,
    bool RunReport,
    string? ReportDirectory,
    string? SummaryReportPath,
    bool RunPhaseFivePc2Lab,
    bool ShowHelp)
{
    public static CliOptions Parse(IEnumerable<string> arguments)
    {
        var array = arguments.ToArray();
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? directory = null;
        string? summary = null;
        string[] allowed = ["--no-ansi", "--smoke-test", "--diagnostic", "--quick-test",
            "--confirm-work-saved", "--phase4-lab-temp", "--pc-manager-detect", "--pc-manager-store",
            "--report", "--phase5-pc2-lab", "--help", "-h"];
        for (var index = 0; index < array.Length; index++)
        {
            var argument = array[index];
            if (argument.Equals("--report-dir", StringComparison.OrdinalIgnoreCase) ||
                argument.Equals("--report-summary", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= array.Length || string.IsNullOrWhiteSpace(array[index + 1]) ||
                    array[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new ArgumentException($"Falta la ruta requerida para {argument}.", nameof(arguments));
                }

                if (!values.Add(argument))
                {
                    throw new ArgumentException($"Opción repetida: {argument}.", nameof(arguments));
                }

                if (argument.Equals("--report-dir", StringComparison.OrdinalIgnoreCase))
                {
                    directory = array[++index];
                }
                else
                {
                    summary = array[++index];
                }
            }
            else if (!allowed.Contains(argument, StringComparer.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Opción desconocida: {argument}. Usa --help.", nameof(arguments));
            }
            else
            {
                values.Add(argument);
            }
        }

        string[] modes = ["--smoke-test", "--diagnostic", "--quick-test", "--phase4-lab-temp",
            "--pc-manager-detect", "--pc-manager-store", "--report", "--report-summary", "--phase5-pc2-lab"];
        if (modes.Count(values.Contains) > 1)
        {
            throw new ArgumentException("Elige un solo modo de ejecución por comando.", nameof(arguments));
        }

        if (values.Contains("--confirm-work-saved") && !values.Contains("--quick-test"))
        {
            throw new ArgumentException("--confirm-work-saved requiere --quick-test.", nameof(arguments));
        }
        return new(
            NoAnsi: values.Contains("--no-ansi"),
            SmokeTest: values.Contains("--smoke-test"),
            RunDiagnostic: values.Contains("--diagnostic"),
            RunStability: values.Contains("--quick-test"),
            ConfirmWorkSaved: values.Contains("--confirm-work-saved"),
            RunPhaseFourLabTemp: values.Contains("--phase4-lab-temp"),
            RunPcManagerDetection: values.Contains("--pc-manager-detect"),
            RunPcManagerStore: values.Contains("--pc-manager-store"),
            RunReport: values.Contains("--report"),
            ReportDirectory: directory,
            SummaryReportPath: summary,
            RunPhaseFivePc2Lab: values.Contains("--phase5-pc2-lab"),
            ShowHelp: values.Contains("--help") || values.Contains("-h"));
    }
}
