using System.Diagnostics;
using System.Text.Json;
using WinMaint.Application;
using WinMaint.Domain;
using WinMaint.Infrastructure.Win;
using WinMaint.Reporting;

// Explicit opt-in only. This tool runs the real Windows CPU workload.
if (args.Length != 2 || args[0] != "--confirm-work-saved")
{
    Console.Error.WriteLine("Usage: --confirm-work-saved NEW_OUTPUT_DIRECTORY");
    return 2;
}

var output = Path.GetFullPath(args[1]);
if (Directory.Exists(output)) throw new IOException("Use a new evidence directory.");
var store = new FileStabilitySessionStore();
if (await store.ReadInterruptedAsync(CancellationToken.None) is not null)
    throw new IOException("An existing session must be reviewed before this verification.");
Directory.CreateDirectory(output);
var journal = new ReportJournal(output);
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
var baseline = await WindowsDiagnosticComposition.Create().CollectAsync(deadline.Token);
journal.AddSnapshot(baseline);
await journal.SaveAsync("InProgress");
var assessment = HealthRuleEngine.Evaluate(baseline);
if (!StabilityPreflightEvaluator.Evaluate(baseline, assessment).CanRun)
{
    await journal.SaveAsync("Partial");
    return 3;
}

using var process = Process.GetCurrentProcess();
using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
var events = new List<StabilityProgress>();
Task? cancelTask = null;
double cpuBefore = 0;
double cpuAtRequest = 0;
TimeSpan? requestAt = null;
var elapsed = Stopwatch.StartNew();
var progress = new CallbackProgress(item =>
{
    events.Add(item);
    Console.WriteLine($"{item.Elapsed}: {item.ProbeId} {item.State}");
    if (item is { ProbeId: "cpu", State: "Iniciando" })
    {
        cpuBefore = process.TotalProcessorTime.TotalSeconds;
        cancelTask = RequestCancellationAsync();
    }
});

async Task RequestCancellationAsync()
{
    await Task.Delay(TimeSpan.FromSeconds(10), deadline.Token);
    cpuAtRequest = process.TotalProcessorTime.TotalSeconds;
    requestAt = elapsed.Elapsed;
    cancellation.Cancel();
}

var result = await WindowsStabilityComposition.Create(store).RunAsync(
    baseline, assessment, progress, cancellation.Token);
var returnedAt = elapsed.Elapsed;
if (cancelTask is not null) await cancelTask;
var cpuAtReturn = process.TotalProcessorTime.TotalSeconds;
await Task.Delay(TimeSpan.FromSeconds(2), deadline.Token);
var cpuAfterReturn = process.TotalProcessorTime.TotalSeconds - cpuAtReturn;
var marker = await store.ReadInterruptedAsync(CancellationToken.None);
journal.AddStability(result);
journal.AddNote("Native cancellation via CancellationToken; does not validate Esc/Ctrl+C keyboard transport or UAC.");
await journal.SaveAsync("Partial");
var passed = requestAt.HasValue && result.WasInterrupted &&
    result.Probes.Single(item => item.ProbeId == "cpu").Status == StabilityProbeStatus.Cancelled &&
    result.Probes.Where(item => item.ProbeId is "memory" or "storage-read")
        .All(item => item.Status == StabilityProbeStatus.Skipped) &&
    cpuAtRequest - cpuBefore > 0.5 &&
    returnedAt - requestAt.Value < TimeSpan.FromSeconds(5) &&
    cpuAfterReturn < 0.25 && marker is not null;
await File.WriteAllTextAsync(Path.Combine(output, "verification.json"), JsonSerializer.Serialize(new
{
    Passed = passed,
    CancellationRequestSeconds = requestAt?.TotalSeconds,
    ReturnSeconds = returnedAt.TotalSeconds,
    CancellationLatencySeconds = requestAt.HasValue ? (returnedAt - requestAt.Value).TotalSeconds : (double?)null,
    CpuSecondsBeforeRequest = cpuAtRequest - cpuBefore,
    CpuSecondsDuringTwoSecondsAfterReturn = cpuAfterReturn,
    InterruptedMarker = marker,
    Progress = events,
}, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
// Archive our own marker before clearing it; never clear an unrelated prior session.
if (marker is not null)
{
    var markerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WinMaint", "stability-session.json");
    File.Copy(markerPath, Path.Combine(output, "interrupted-session.json"), overwrite: false);
    await store.CompleteAsync(CancellationToken.None);
}
Console.WriteLine($"Native cancellation verification passed: {passed}");
return passed ? 0 : 1;

internal sealed class CallbackProgress(Action<StabilityProgress> callback) : IProgress<StabilityProgress>
{
    public void Report(StabilityProgress value) => callback(value);
}
