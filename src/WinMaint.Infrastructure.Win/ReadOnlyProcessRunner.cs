using System.Diagnostics;
using System.Text;

namespace WinMaint.Infrastructure.Win;

internal sealed record ControlledProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut);

internal static class ControlledProcessRunner
{
    public static async Task<ControlledProcessResult> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Path.IsPathFullyQualified(executablePath))
        {
            throw new ArgumentException("La ruta del ejecutable debe ser absoluta.", nameof(executablePath));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException($"No se pudo iniciar {Path.GetFileName(executablePath)}.");
        }

        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryTerminate(process);
            return new ControlledProcessResult(-1, string.Empty, "Tiempo de espera agotado.", TimedOut: true);
        }
        catch (OperationCanceledException)
        {
            TryTerminate(process);
            throw;
        }

        return new ControlledProcessResult(
            process.ExitCode,
            await standardOutput.ConfigureAwait(false),
            await standardError.ConfigureAwait(false),
            TimedOut: false);
    }

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // El proceso terminó entre la comprobación y la cancelación.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // El resultado sigue siendo cancelado; no se oculta como éxito.
        }
    }
}

internal static class PowerShellJsonRunner
{
    private static readonly string PowerShellPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        "WindowsPowerShell",
        "v1.0",
        "powershell.exe");

    public static Task<ControlledProcessResult> RunAsync(
        string trustedScript,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var script = "[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false); " + trustedScript;
        var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        string[] arguments =
        [
            "-NoLogo",
            "-NoProfile",
            "-NonInteractive",
            "-ExecutionPolicy",
            "Bypass",
            "-EncodedCommand",
            encodedCommand,
        ];

        return ControlledProcessRunner.RunAsync(PowerShellPath, arguments, timeout, cancellationToken);
    }
}
