using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using WinMaint.Application;
using WinMaint.Domain;
using WinMaint.Infrastructure.Win;

bool hostRead = args.Length == 1 && args[0] == "--host-read-observation" &&
    Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory).EndsWith(
        Path.Combine("artifacts", "lab", "probe-effects-02-a1"), StringComparison.OrdinalIgnoreCase);
bool sandbox = Environment.UserName == "WDAGUtilityAccount" &&
    AppContext.BaseDirectory.Equals(@"C:\WinMaintProbeEffectsV2\", StringComparison.OrdinalIgnoreCase);
bool control = sandbox && args.Length == 1 && args[0] == "--control-write";
if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess || !(hostRead || control))
{ Console.WriteLine("BLOCKED: fixed host read-only observation or owned Sandbox write control only."); return 3; }

string id = Guid.NewGuid().ToString("N");
string output = hostRead ? Path.Combine(AppContext.BaseDirectory, "observations") : @"C:\WinMaintLabOutput";
Directory.CreateDirectory(output);
string work = hostRead ? Path.Combine(output, "owned-work-" + id) : Path.Combine(Path.GetTempPath(), "WinMaintProbeEffects", id);
Directory.CreateDirectory(work);
var baseline = await WindowsDiagnosticComposition.Create().CollectAsync();
var preflight = StabilityPreflightEvaluator.Evaluate(baseline, HealthRuleEngine.Evaluate(baseline));
if (!control && !preflight.CanRun)
{
    await File.WriteAllTextAsync(Path.Combine(output, "probe-effects-" + id + ".json"),
        JsonSerializer.Serialize(new { Passed = false, Reason = "Native preflight blocked", preflight }));
    return 4;
}
var context = new StabilityProbeContext(baseline, StabilityTestOptions.Standard);
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
if (!control) _ = await new StorageReadProbe(TimeSpan.FromMilliseconds(20)).ExecuteAsync(context, deadline.Token); // Warm setup before observation.
using var process = Process.GetCurrentProcess();
string executable = Environment.ProcessPath!;
string Hash() { using var stream = File.OpenRead(executable); return Convert.ToHexString(SHA256.HashData(stream)); }
string hashBefore = Hash();
long sizeBefore = new FileInfo(executable).Length;
DateTime writeBefore = File.GetLastWriteTimeUtc(executable);
_ = NativeIo.Read(process.Handle); // Warm the observation API too.
var before = NativeIo.Read(process.Handle);
var result = control ? null : await new StorageReadProbe(TimeSpan.FromMilliseconds(125)).ExecuteAsync(context, deadline.Token);
if (control) await File.WriteAllBytesAsync(Path.Combine(work, "control.bin"), new byte[4096], deadline.Token);
var after = NativeIo.Read(process.Handle);
ulong writeOperations = after.WriteOperations - before.WriteOperations;
ulong writeBytes = after.WriteBytes - before.WriteBytes;
ulong readOperations = after.ReadOperations - before.ReadOperations;
ulong readBytes = after.ReadBytes - before.ReadBytes;
var filesAfter = Directory.GetFiles(work);
bool fileUnchanged = Hash() == hashBefore && new FileInfo(executable).Length == sizeBefore && File.GetLastWriteTimeUtc(executable) == writeBefore;
bool passed = fileUnchanged && (control ? writeOperations > 0 && writeBytes >= 4096 && filesAfter.Length == 1 :
    result!.Status == StabilityProbeStatus.Passed && result.Metrics["storage.bytes-read"] > 0 && readOperations > 0 && readBytes > 0 &&
    writeOperations == 0 && writeBytes == 0 && filesAfter.Length == 0);
await File.WriteAllTextAsync(Path.Combine(output, "probe-effects-" + id + ".json"), JsonSerializer.Serialize(new
{
    SchemaVersion = "probe-effects-1.0", Scenario = control ? "write-control" : "native-storage-read", Passed = passed,
    ExecutableSha256 = hashBefore, PreflightCanRun = preflight.CanRun,
    Probe = result, WriteOperationsDelta = writeOperations,
    WriteBytesDelta = writeBytes, ReadOperationsDelta = readOperations, ReadBytesDelta = readBytes,
    ExecutableUnchanged = fileUnchanged, OwnedWorkingDirectoryFiles = filesAfter.Length,
    Scope = control ? "Isolated 4096-byte write control in disposable VM; no workload probe executed." :
        "Own process IO during a 125 ms native read probe after real preflight; not global IO, physical disk coverage or certification.",
}, new JsonSerializerOptions { WriteIndented = true }));
return passed ? 0 : 1;

internal static class NativeIo
{
    internal static Counters Read(IntPtr process)
    {
        if (!GetProcessIoCounters(process, out var counters)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return counters;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Counters
    {
        internal ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
    }
#pragma warning disable SYSLIB1054 // Explicit Win32 IO_COUNTERS ABI for this isolated observer.
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessIoCounters(IntPtr process, out Counters counters);
#pragma warning restore SYSLIB1054
}
