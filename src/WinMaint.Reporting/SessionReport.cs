using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using WinMaint.Application;
using WinMaint.Domain;

namespace WinMaint.Reporting;

public sealed record ReportSnapshot(DiagnosticReport Diagnostic, HealthAssessment Health);

public sealed record MetricComparison(string Key, string Source, double Before, double After, double Delta);

public sealed record PcManagerDecision(PcManagerDetectionResult Detection, PcManagerActionPlan Plan);

public sealed record SessionReport(
    string SchemaVersion,
    Guid SessionId,
    string ToolVersion,
    string Machine,
    string OperatingSystem,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt,
    string Status,
    IReadOnlyList<ReportSnapshot> Snapshots,
    IReadOnlyList<PcManagerActionResult> Actions,
    IReadOnlyList<PcManagerDecision> Decisions,
    IReadOnlyList<StabilityTestResult> StabilityTests,
    IReadOnlyList<string> Notes)
{
    public IReadOnlyList<TemporaryCleanupRecord> TemporaryCleanup { get; init; } = [];
    public HealthAssessment? FinalHealth { get; init; }
    public Guid? ExportId { get; init; }
    public IReadOnlyList<MetricComparison> Comparisons => ReportComparison.Compare(Snapshots);
}

public static class ReportComparison
{
    public static IReadOnlyList<MetricComparison> Compare(IReadOnlyList<ReportSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        if (snapshots.Count < 2)
        {
            return [];
        }

        var before = Metrics(snapshots[0]);
        var after = Metrics(snapshots[^1]);
        return before.Where(pair => after.ContainsKey(pair.Key))
            .Select(pair => new MetricComparison(pair.Key.Key, pair.Key.Source,
                pair.Value, after[pair.Key], after[pair.Key] - pair.Value)).ToArray();
    }

    private static Dictionary<(string Key, string Source), double> Metrics(ReportSnapshot snapshot) =>
        snapshot.Diagnostic.Sections.SelectMany(section => section.Data
            .Where(datum => datum.Coverage == EvidenceCoverage.Complete &&
                datum.NumericValue.HasValue && double.IsFinite(datum.NumericValue.Value) &&
                datum.MetricKey is not null &&
                (datum.MetricKey.EndsWith(".bytes", StringComparison.Ordinal) ||
                 datum.MetricKey.EndsWith(".percent", StringComparison.Ordinal) ||
                 datum.MetricKey.EndsWith(".count", StringComparison.Ordinal)))
            .Select(datum => (Key: (Key: section.Id + "/" + datum.Name + "/" + datum.MetricKey,
                Source: datum.Source), Value: datum.NumericValue!.Value)))
            .GroupBy(item => item.Key).Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single().Value);
}

public static class ReportRenderer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string ToJson(SessionReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var node = JsonSerializer.SerializeToNode(report, Options)!;
        Redact(node);
        return node.ToJsonString(Options);
    }

    public static string ToText(SessionReport report)
    {
        var safe = Parse(ToJson(report));
        var latest = safe.Snapshots.Count == 0 ? null : safe.Snapshots[^1];
        var text = new StringBuilder();
        var system = latest?.Diagnostic.Sections.SelectMany(section => section.Data)
            .FirstOrDefault(datum => datum.Name == "Sistema operativo")?.Value ?? safe.OperatingSystem;
        text.AppendLine("Hecho por Jafet").AppendLine("WinMaint — Resumen del equipo")
            .AppendLine(CultureInfo.InvariantCulture, $"{safe.Machine} | {system} | {safe.UpdatedAt:dd/MM/yyyy HH:mm}")
            .AppendLine(StatusLabel(safe.Status)).AppendLine();

        var supportNotice = latest?.Diagnostic.Sections.SelectMany(section => section.Data)
            .FirstOrDefault(datum => datum.MetricKey == "system.support.notice");
        if (supportNotice is not null)
            text.AppendLine(supportNotice.Value).AppendLine("Inscripción ESU: desconocida; no comprobada por WinMaint.").AppendLine();

        text.AppendLine("ESTADO DEL EQUIPO");
        if (latest is null)
        {
            text.AppendLine("Desconocido: no se completó un diagnóstico.");
        }
        else
        {
            var health = safe.FinalHealth ?? LegacyHealth(safe);
            var needsAttention = health.Findings.Any(finding => finding.Priority != Priority.P3);
            text.AppendLine(needsAttention ? "Hay señales que requieren revisión." :
                health.OverallScore.HasValue ? "Sin problemas urgentes detectados en los datos disponibles." :
                "Datos insuficientes para evaluar el estado del equipo.");
            if (health.OverallScore.HasValue)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"Evaluación orientativa: {health.OverallScore}/100.");
            }

            text.AppendLine(CultureInfo.InvariantCulture, $"Información disponible: {health.ConfidencePercent}% de la cobertura prevista. No es un porcentaje de certeza ni una garantía de salud.");
            if (health.MemoryEvidence is { } memory) text.AppendLine(memory.Summary);
        }

        text.AppendLine().AppendLine("QUÉ REQUIERE ATENCIÓN");
        var findings = (safe.FinalHealth?.Findings ?? safe.Snapshots.SelectMany(snapshot => snapshot.Health.Findings)
            .Concat(safe.StabilityTests.SelectMany(test => test.Findings)).ToArray())
            .GroupBy(finding => finding.RuleId)
            .Select(group => group.OrderBy(finding => finding.Priority).First())
            .OrderBy(finding => finding.Priority).ToArray();
        if (findings.Length == 0)
        {
            text.AppendLine(latest is null ? "Aún no hay un diagnóstico para revisar." : "No se registraron alertas en las comprobaciones realizadas.");
        }

        foreach (var finding in findings)
        {
            var summary = finding.RuleId == "WHEA-RECENT"
                ? "Windows registró avisos relacionados con el hardware. Por sí solos no confirman una pieza dañada."
                : finding.Summary;
            var next = finding.RuleId == "WHEA-RECENT"
                ? "Revisar esos avisos y los controladores antes de concluir que el equipo es estable."
                : finding.Recommendation;
            text.AppendLine(CultureInfo.InvariantCulture, $"- {PriorityLabel(finding.Priority)}: {summary}");
            text.AppendLine(CultureInfo.InvariantCulture, $"  Siguiente paso: {next}");
        }

        text.AppendLine().AppendLine("QUÉ SE HIZO");
        text.AppendLine(CultureInfo.InvariantCulture, $"- Diagnósticos completados: {safe.Snapshots.Count}.");
        if (safe.StabilityTests.Count == 0)
        {
            text.AppendLine("- Prueba de estabilidad: no ejecutada.");
        }

        foreach (var test in safe.StabilityTests)
        {
            var result = test.WasInterrupted ? "interrumpida" :
                test.Probes.Any(probe => probe.Status is StabilityProbeStatus.Failed or StabilityProbeStatus.TimedOut) ? "con fallos o tiempo agotado" :
                test.Probes.Any(probe => probe.Status is StabilityProbeStatus.Cancelled or StabilityProbeStatus.Skipped) ? "incompleta" :
                test.Probes.Count == 0 ? "sin resultados" : "terminada sin fallos detectados durante esta prueba";
            text.AppendLine(CultureInfo.InvariantCulture, $"- Prueba de estabilidad: {result}.");
        }

        if (safe.Actions.Count == 0 && safe.TemporaryCleanup.All(item => item.Status == "InventoryOnly"))
        {
            text.AppendLine("- No se registraron acciones externas ni mantenimiento ejecutado por WinMaint.");
        }

        foreach (var action in safe.Actions)
        {
            var name = action.Action switch
            {
                PcManagerProposedAction.LaunchInstalled => "Abrir PC Manager",
                PcManagerProposedAction.OpenOfficialStorePage => "Abrir la ficha de Microsoft Store",
                _ => "Acción de PC Manager",
            };
            var outcome = action.Outcome switch
            {
                PcManagerActionOutcome.Succeeded => "Windows aceptó la solicitud de apertura",
                PcManagerActionOutcome.Cancelled => "cancelada",
                PcManagerActionOutcome.Blocked => "bloqueada",
                _ => "falló; consultar el detalle técnico",
            };
            text.AppendLine(CultureInfo.InvariantCulture, $"- {name}: {outcome}.");
        }

        foreach (var cleanup in safe.TemporaryCleanup)
        {
            var coverage = cleanup.Inventory.Coverage switch
            {
                EvidenceCoverage.Complete => "recorrido terminado",
                EvidenceCoverage.Partial => "recorrido incompleto",
                _ => "datos insuficientes",
            };
            var status = cleanup.Status switch
            {
                "Blocked" => "bloqueada",
                "Cancelled" => "cancelada",
                "Completed" => "terminada",
                "FinishedWithSkips" => "terminada con omisiones",
                "Interrupted" => "interrumpida",
                _ => "fallida",
            };
            if (cleanup.Status == "InventoryOnly")
                text.AppendLine(CultureInfo.InvariantCulture, $"- Inventario de temporales: {cleanup.Inventory.CandidateFiles?.ToString(CultureInfo.InvariantCulture) ?? "desconocido"} candidatos ({coverage}); no se borró nada.");
            else
                text.AppendLine(CultureInfo.InvariantCulture, $"- Temporales: {status}; {cleanup.DeletedFiles} eliminaciones verificadas, {cleanup.DeletedLogicalBytes} bytes lógicos. Espacio físico no medido.");
            if (cleanup.Files.Any(file => file.Outcome == TemporaryFileOutcome.Unverified))
                text.AppendLine("  Hay operaciones sin verificación; no se presume que dejaron los archivos intactos.");
            if (cleanup.JournalAttemptId.HasValue)
                text.AppendLine(CultureInfo.InvariantCulture, $"  Registro de intento: {cleanup.JournalAttemptId:N}; no intentados: {cleanup.NotAttemptedCandidates.Count}. No se reanuda automáticamente.");
        }

        text.AppendLine().AppendLine("QUÉ CAMBIÓ");
        var visible = safe.Comparisons.Where(metric => IsVisibleChange(metric)).ToArray();
        if (safe.Comparisons.Count == 0)
        {
            text.AppendLine("No hay dos capturas equivalentes para comparar.");
        }
        else if (visible.Length == 0)
        {
            text.AppendLine("Sin cambios visibles en las métricas comparables al redondearlas.");
        }

        foreach (var metric in visible)
        {
            var label = metric.Key.Split('/')[1];
            text.AppendLine(CultureInfo.InvariantCulture, $"- {label}: {MetricText(metric.Before, metric.Key)} → {MetricText(metric.After, metric.Key)}.");
        }

        if (safe.Comparisons.Count > 0)
        {
            text.AppendLine("Son variaciones entre mediciones; no demuestran una mejora causada por mantenimiento.");
        }

        foreach (var note in safe.Notes.Distinct(StringComparer.Ordinal))
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"Nota: {note}");
        }

        text.AppendLine().AppendLine("El archivo JSON conserva las mediciones, fuentes y reglas para revisión técnica.");
        if (safe.ExportId is Guid exportId) text.AppendLine(CultureInfo.InvariantCulture, $"Exportación: {exportId:N}.");
        return text.ToString();
    }

    public static bool IsMatchingExport(string json, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var report = Parse(json);
        return report.ExportId is Guid exportId &&
            text.Contains($"Exportación: {exportId:N}.", StringComparison.Ordinal);
    }

    private static string StatusLabel(string status) => status switch
    {
        "Completed" => "Sesión cerrada.",
        "Partial" => "Reporte parcial: sesión abierta o información incompleta.",
        "InProgress" => "Operación sin cierre registrado; pudo interrumpirse.",
        _ => "Estado de sesión desconocido.",
    };

    private static string PriorityLabel(Priority priority) => priority switch
    {
        Priority.P0 => "Atención inmediata",
        Priority.P1 => "Problema importante",
        Priority.P2 => "Revisar",
        _ => "Aviso",
    };

    private static bool IsVisibleChange(MetricComparison metric) =>
        !string.Equals(MetricText(metric.Before, metric.Key), MetricText(metric.After, metric.Key), StringComparison.Ordinal);

    private static string MetricText(double value, string key) => key.EndsWith(".bytes", StringComparison.Ordinal)
        ? (value / 1073741824d).ToString("0.00", CultureInfo.InvariantCulture) + " GiB"
        : value.ToString("0.0", CultureInfo.InvariantCulture) +
            (key.EndsWith(".percent", StringComparison.Ordinal) ? "%" : " eventos");

    public static SessionReport Parse(string json)
    {
        var report = JsonSerializer.Deserialize<SessionReport>(json, Options) ?? throw new JsonException("Reporte vacío.");
        if (report.SchemaVersion is "1.1" or "1.2")
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("temporaryCleanup", out var cleanup) || cleanup.ValueKind != JsonValueKind.Array)
                throw new JsonException("El esquema requiere resultados de temporales explícitos.");
            if (report.SchemaVersion == "1.2" &&
                !document.RootElement.TryGetProperty("finalHealth", out _))
                throw new JsonException("El esquema 1.2 requiere evaluación final explícita.");
        }
        if (report.SchemaVersion is not ("1.0" or "1.1" or "1.2") || report.TemporaryCleanup is null ||
            (report.SchemaVersion == "1.0" && report.TemporaryCleanup.Count != 0) ||
            report.TemporaryCleanup.Any(item => item is null || item.Inventory is null || item.Files is null ||
                item.Status is not ("InventoryOnly" or "Blocked" or "Cancelled" or "Completed" or "FinishedWithSkips" or "Failed" or "Interrupted") ||
                item.Files.Any(file => file is null || !Enum.IsDefined(file.Outcome) ||
                    (file.Outcome == TemporaryFileOutcome.DeletedVerified ? file.DeletedLogicalBytes is null or < 0 : file.DeletedLogicalBytes is not null and not 0))) ||
            report.Snapshots is null || report.Actions is null ||
            report.Decisions is null || report.StabilityTests is null || report.Notes is null ||
            report.Status is not ("Completed" or "Partial" or "InProgress") ||
            report.Snapshots.Any(snapshot => snapshot is null || snapshot.Diagnostic is null ||
                snapshot.Health is null || snapshot.Diagnostic.Sections is null ||
                snapshot.Health.Findings is null || snapshot.Health.Domains is null ||
                !ValidMemoryEvidence(snapshot.Health.MemoryEvidence) ||
                snapshot.Health.Findings.Any(finding => finding is null) ||
                snapshot.Diagnostic.Sections.Any(section => section is null || section.Data is null ||
                    section.Notes is null || section.Data.Any(datum => datum is null))) ||
            report.Actions.Any(action => action is null) ||
            report.Decisions.Any(decision => decision is null || decision.Detection is null || decision.Plan is null) ||
            report.StabilityTests.Any(test => test is null || test.Probes is null || test.Findings is null ||
                test.Probes.Any(probe => probe is null) || test.Findings.Any(finding => finding is null)))
        {
            throw new JsonException("El reporte no cumple el esquema 1.0/1.1/1.2.");
        }

        if (report.SchemaVersion == "1.2" &&
            ((report.Snapshots.Count == 0) != (report.FinalHealth is null) ||
             report.FinalHealth is { } final && (!ValidFinalHealth(final) ||
                !report.Snapshots.SelectMany(snapshot => snapshot.Health.Findings)
                    .Concat(report.StabilityTests.SelectMany(test => test.Findings)).All(finding =>
                        final.Findings.Any(retained => retained.RuleId == finding.RuleId &&
                            retained.Domain == finding.Domain && retained.Evidence == finding.Evidence &&
                            retained.Priority <= finding.Priority)))))
            throw new JsonException("Evaluación final inválida.");

        if (report.SchemaVersion is "1.1" or "1.2")
        {
            using var document = JsonDocument.Parse(json);
            var raw = document.RootElement.GetProperty("temporaryCleanup");
            for (var index = 0; index < report.TemporaryCleanup.Count; index++)
                TemporaryCleanupReportValidation.Validate(report.TemporaryCleanup[index], raw[index]);
        }

        return report;
    }

    private static bool ValidFinalHealth(HealthAssessment health) =>
        !string.IsNullOrWhiteSpace(health.RuleSetVersion) && !string.IsNullOrWhiteSpace(health.Band) &&
        ValidMemoryEvidence(health.MemoryEvidence) &&
        health.Findings is not null && health.Domains is not null &&
        health.Findings.All(finding => finding is not null && Enum.IsDefined(finding.Priority) &&
            Enum.IsDefined(finding.Domain) && Enum.IsDefined(finding.SuggestedActionRisk)) &&
        health.Domains.Count == Enum.GetValues<HealthDomain>().Length &&
        health.Domains.All(domain => domain is not null && Enum.IsDefined(domain.Domain) &&
            domain.ConfidencePercent is >= 0 and <= 100 && domain.Score is not (< 0 or > 100) &&
            domain.HasSufficientEvidence == domain.Score.HasValue) &&
        health.Domains.Select(domain => domain.Domain).Distinct().Count() == health.Domains.Count &&
        health.OverallScore is not (< 0 or > 100) && health.ConfidencePercent is >= 0 and <= 100 &&
        !(health.OverallScore > 39 && health.Findings.Any(finding => finding.Priority == Priority.P0)) &&
        !(health.OverallScore > 59 && health.Findings.Any(finding => finding.Priority == Priority.P1));

    private static bool ValidMemoryEvidence(MemoryEvidenceAssessment? memory) => memory is null ||
        (memory.CurrentLoadPercent is null || double.IsFinite(memory.CurrentLoadPercent.Value) &&
            memory.CurrentLoadPercent is >= 0 and <= 100) &&
        memory.HighUseInCurrentSample == (memory.CurrentLoadPercent.HasValue
            ? memory.CurrentLoadPercent >= MemoryEvidencePolicy.HighCurrentUsePercent : (bool?)null) &&
        memory.SustainedPressureCoverage == EvidenceCoverage.Absent && Enum.IsDefined(memory.PhysicalState) &&
        memory.PreviousDiagnosticStatus is "unknown" or "passed" or "failed" &&
        memory.PhysicalCoveragePercent == (memory.PreviousDiagnosticStatus == "unknown" ? 0 : 50) &&
        (memory.PhysicalState != MemoryPhysicalState.NoFaultsObservedInPreviousTest || memory.PreviousDiagnosticStatus == "passed") &&
        (memory.PhysicalState != MemoryPhysicalState.InsufficientEvidence || memory.PreviousDiagnosticStatus == "unknown") &&
        (memory.ProcessPatternStatus == "NotRun" || Enum.GetNames<StabilityProbeStatus>().Contains(memory.ProcessPatternStatus, StringComparer.Ordinal));

    private static HealthAssessment LegacyHealth(SessionReport report)
    {
        // Preserve stored historical scores/rules. Only enforce unresolved priority caps.
        var latest = report.Snapshots[^1].Health;
        var findings = report.Snapshots.SelectMany(snapshot => snapshot.Health.Findings)
            .Concat(report.StabilityTests.SelectMany(test => test.Findings)).ToArray();
        var score = latest.OverallScore;
        if (score.HasValue && findings.Any(finding => finding.Priority == Priority.P0)) score = Math.Min(score.Value, 39);
        else if (score.HasValue && findings.Any(finding => finding.Priority == Priority.P1)) score = Math.Min(score.Value, 59);
        return latest with { OverallScore = score, Findings = findings };
    }

    private static bool IsSerial(string key) => key.Contains("serial", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("serie", StringComparison.OrdinalIgnoreCase);

    private static void Redact(JsonNode node)
    {
        var secrets = new HashSet<string>(StringComparer.Ordinal);
        Collect(node, secrets);
        Scrub(node, secrets);
    }

    private static void Collect(JsonNode node, HashSet<string> secrets)
    {
        if (node is JsonObject obj)
        {
            var serialDatum = obj["name"] is JsonValue name && name.TryGetValue<string>(out var label) && IsSerial(label);
            if (serialDatum)
            {
                obj["numericValue"] = null;
            }
            foreach (var pair in obj.ToArray())
            {
                if (pair.Value is JsonValue value && value.TryGetValue<string>(out var secret) &&
                    (IsSerial(pair.Key) || (serialDatum && pair.Key is "value" or "textValue" or "detail")))
                {
                    if (!string.IsNullOrWhiteSpace(secret))
                    {
                        secrets.Add(secret);
                    }

                    obj[pair.Key] = "[SERIAL OCULTO]";
                }
                else if (pair.Value is not null)
                {
                    Collect(pair.Value, secrets);
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array.OfType<JsonNode>())
            {
                Collect(item, secrets);
            }
        }
    }

    private static void Scrub(JsonNode node, HashSet<string> secrets)
    {
        if (node is JsonObject obj)
        {
            foreach (var pair in obj.ToArray())
            {
                if (pair.Value is JsonValue value && value.TryGetValue<string>(out var text))
                {
                    foreach (var secret in secrets.OrderByDescending(secret => secret.Length))
                    {
                        text = text.Replace(secret, "[SERIAL OCULTO]", StringComparison.Ordinal);
                    }

                    obj[pair.Key] = text;
                }
                else if (pair.Value is not null)
                {
                    Scrub(pair.Value, secrets);
                }
            }
        }
        else if (node is JsonArray array)
        {
            for (var index = 0; index < array.Count; index++)
            {
                if (array[index] is JsonValue value && value.TryGetValue<string>(out var text))
                {
                    foreach (var secret in secrets.OrderByDescending(secret => secret.Length))
                    {
                        text = text.Replace(secret, "[SERIAL OCULTO]", StringComparison.Ordinal);
                    }

                    array[index] = text;
                }
                else if (array[index] is JsonNode item)
                {
                    Scrub(item, secrets);
                }
            }
        }
    }
}
