using System.Diagnostics;
using System.Security.Cryptography;

namespace WinMaint.ReviewRegression.Tests;

public sealed class ProcessHelperRegressionTests
{
    public static TheoryData<string, bool, string> Cases
    {
        get
        {
            var data = new TheoryData<string, bool, string>();
            foreach (var host in new[] { "WindowsPowerShell", "PowerShell7" })
                foreach (var bom in new[] { false, true })
                    foreach (var scenario in new[] { "menu", "eof", "arguments", "nonzero", "timeout", "invalid-input", "unsupported-argument", "hash-match", "hash-mismatch" })
                        data.Add(host, bom, scenario);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task HelperIsExercisedWithOwnChildAndActualConsumedBytes(string host, bool bom, string scenario)
    {
        var executable = host == "WindowsPowerShell"
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe")
            : FindPowerShell7();
        Assert.True(File.Exists(executable), "Required PowerShell host missing: " + host);
        var directory = Path.Combine(Path.GetTempPath(), "WinMaintProcessHelperTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var fixtureExe = Path.Combine(AppContext.BaseDirectory, "process-fixture", "WinMaint.ProcessFixture.exe");
        var expectedHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(fixtureExe)));
        try
        {
            var start = new ProcessStartInfo(executable)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
                Path.Combine(AppContext.BaseDirectory, "TEST-PROCESS-HELPER.ps1"), "-FixtureExe",
                fixtureExe, "-HelperPath", Path.Combine(AppContext.BaseDirectory, "Invoke-CandidateProcess.ps1"),
                "-Case", scenario, "-FixtureDirectory", directory, "-ExpectedFixtureSha256", expectedHash })
                start.ArgumentList.Add(argument);
            if (bom) start.ArgumentList.Add("-WithBom");
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try { await process.WaitForExitAsync(deadline.Token); }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true); // Only this owned harness and its fixture child.
                    await process.WaitForExitAsync();
                }
            }
            var output = await stdout;
            var error = await stderr;
            Assert.True(process.ExitCode == 0, output + error);
            Assert.Contains("PASSED:" + scenario, output);
            Assert.Contains("PARENT_PREAMBLE:" + (bom ? "3" : "0"), output);
            Assert.Contains("HOST_MAJOR:" + (host == "WindowsPowerShell" ? "5" : "7"), output);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static string FindPowerShell7() =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator)
            .Where(folder => !string.IsNullOrWhiteSpace(folder))
            .Select(folder => Path.Combine(folder.Trim('"'), "pwsh.exe"))
            .FirstOrDefault(File.Exists) ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "PowerShell", "7", "pwsh.exe");
}
