using System.Text;
using WinMaint.Application;
using WinMaint.Domain;

namespace WinMaint.Reporting;

public sealed record ReportSaveResult(bool JsonSaved, bool TextSaved, string? ErrorType)
{
    public bool Succeeded => JsonSaved && TextSaved;
    public int ExportExitCode => Succeeded ? 0 : 4;
}

public interface IReportFileWriter
{
    Task WriteAsync(string path, string content, CancellationToken cancellationToken);
}

public sealed class ReportJournal(string directory, IReportFileWriter? writer = null)
{
    private readonly IReportFileWriter writer = writer ?? new AtomicReportFileWriter();
    private readonly List<ReportSnapshot> snapshots = [];
    private readonly List<PcManagerActionResult> actions = [];
    private readonly List<PcManagerDecision> decisions = [];
    private readonly List<StabilityTestResult> tests = [];
    private readonly List<string> notes = [];
    private readonly List<TemporaryCleanupRecord> temporaryCleanup = [];
    private readonly Guid sessionId = Guid.NewGuid();
    private readonly DateTimeOffset startedAt = DateTimeOffset.Now;

    public string JsonPath => Path.Combine(directory, $"WinMaint-{sessionId:N}.json");
    public string TextPath => Path.Combine(directory, $"WinMaint-{sessionId:N}.txt");
    public HealthAssessment? FinalHealth => HealthRuleEngine.EvaluateSession(
        snapshots.Select(snapshot => snapshot.Health).ToArray(), tests);

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "WinMaint", "Reports");

    public void AddSnapshot(DiagnosticReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        snapshots.Add(new ReportSnapshot(report, HealthRuleEngine.Evaluate(report)));
    }

    public void AddAction(PcManagerActionResult result) => actions.Add(result);
    public void AddDecision(PcManagerDetectionResult detection, PcManagerActionPlan plan) => decisions.Add(new(detection, plan));
    public void AddStability(StabilityTestResult result) => tests.Add(result);
    public void AddNote(string note) => notes.Add(note);
    public void AddTemporaryCleanup(TemporaryCleanupRecord result) => temporaryCleanup.Add(result);

    public void AddPreparedTemporaryInventory(TemporaryInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        temporaryCleanup.Add(new(inventory, "InventoryOnly", [], "Solo metadata. Limpieza de producción bloqueada."));
    }

    public void AddTemporaryInventory(DiagnosticSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        long? Count(string key)
        {
            var data = section.Data.Where(datum => datum.MetricKey == key).ToArray();
            if (data.Length != 1 || data[0].NumericValue is not double value || !double.IsFinite(value) ||
                value < 0 || value >= long.MaxValue || value != Math.Truncate(value)) return null;
            return (long)value;
        }
        var inventory = new TemporaryInventory(Guid.NewGuid(), DateTimeOffset.UtcNow, section.Coverage,
            Count("temp.preview.files.count"), Count("temp.preview.bytes"), Count("temp.preview.recent.count"),
            Count("temp.preview.protected.count"), Count("temp.preview.unavailable.count"));
        AddPreparedTemporaryInventory(inventory);
    }

    public SessionReport CreateReport(string status) => new(
        "1.2", sessionId, ProductIdentity.DisplayVersion, Environment.MachineName,
        Environment.OSVersion.VersionString, startedAt, DateTimeOffset.Now, status,
        snapshots.ToArray(), actions.ToArray(), decisions.ToArray(), tests.ToArray(), notes.ToArray())
    { TemporaryCleanup = temporaryCleanup.ToArray(), FinalHealth = FinalHealth, ExportId = Guid.NewGuid() };

    // Existing lab callers rely on failure throwing before they may proceed.
    public async Task SaveAsync(string status, CancellationToken cancellationToken = default)
    {
        var result = await TrySaveAsync(status, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded) throw new IOException("Exportación incompleta: " + result.ErrorType);
    }

    public async Task<ReportSaveResult> TrySaveAsync(string status, CancellationToken cancellationToken = default)
    {
        var report = CreateReport(status);
        var jsonSaved = false;
        try
        {
            // JSON remains authoritative. A second-write failure is explicitly partial.
            await writer.WriteAsync(JsonPath, ReportRenderer.ToJson(report), cancellationToken).ConfigureAwait(false);
            jsonSaved = true;
            await writer.WriteAsync(TextPath, ReportRenderer.ToText(report), cancellationToken).ConfigureAwait(false);
            return new(true, true, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return new(jsonSaved, false, exception.GetType().Name);
        }
    }

    private sealed class AtomicReportFileWriter : IReportFileWriter
    {
        public async Task WriteAsync(string path, string content, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temporary, content, new UTF8Encoding(true), cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporary, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}
