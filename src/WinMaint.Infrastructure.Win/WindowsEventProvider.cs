using System.Globalization;
using System.Xml.Linq;
using WinMaint.Application;
using WinMaint.Domain;

namespace WinMaint.Infrastructure.Win;

internal enum WheaEventKind
{
    Error,
    Informational,
    Unknown,
}

internal sealed record WindowsEventRecord(
    int EventId,
    int Level,
    string Provider,
    DateTimeOffset? CreatedAt,
    WheaEventKind WheaKind = WheaEventKind.Unknown);

internal static class WindowsEventXmlParser
{
    private static readonly XNamespace EventNamespace = "http://schemas.microsoft.com/win/2004/08/events/event";

    internal static IReadOnlyList<WindowsEventRecord> Parse(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return [];
        }

        var sanitized = xml.Replace("<?xml version=\"1.0\" encoding=\"utf-8\" standalone=\"yes\"?>", string.Empty, StringComparison.OrdinalIgnoreCase);
        var document = XDocument.Parse("<WinMaintEvents>" + sanitized + "</WinMaintEvents>", LoadOptions.None);
        return document
            .Descendants(EventNamespace + "Event")
            .Select(ParseEvent)
            .Where(item => item is not null)
            .Cast<WindowsEventRecord>()
            .ToArray();
    }

    private static WindowsEventRecord? ParseEvent(XElement element)
    {
        var system = element.Element(EventNamespace + "System");
        var eventIdText = system?.Element(EventNamespace + "EventID")?.Value;
        var levelText = system?.Element(EventNamespace + "Level")?.Value;
        if (!int.TryParse(eventIdText, NumberStyles.None, CultureInfo.InvariantCulture, out var eventId)
            || !int.TryParse(levelText, NumberStyles.None, CultureInfo.InvariantCulture, out var level))
        {
            throw new System.Xml.XmlException("Evento sin identificador o nivel válido.");
        }

        var provider = system?.Element(EventNamespace + "Provider")?.Attribute("Name")?.Value ?? "Proveedor desconocido";
        var createdText = system?.Element(EventNamespace + "TimeCreated")?.Attribute("SystemTime")?.Value;
        DateTimeOffset? createdAt = DateTimeOffset.TryParse(
            createdText,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : null;
        return new WindowsEventRecord(eventId, level, provider, createdAt, ClassifyWhea(element, level));
    }

    private static WheaEventKind ClassifyWhea(XElement element, int level)
    {
        var raw = element.Element(EventNamespace + "EventData")?
            .Elements(EventNamespace + "Data")
            .FirstOrDefault(data => data.Attribute("Name")?.Value == "RawData")?.Value;
        if (!string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                var bytes = Convert.FromHexString(raw);
                // CPER: cabecera de 128 bytes; severidad en offset 12 y longitud en 20.
                if (bytes.Length >= 128
                    && bytes.AsSpan(0, 4).SequenceEqual("CPER"u8)
                    && System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(6, 4)) == uint.MaxValue
                    && System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(20, 4)) == bytes.Length)
                {
                    var severity = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12, 4));
                    if (severity <= 2)
                    {
                        return WheaEventKind.Error;
                    }

                    if (severity == 3 && level == 4)
                    {
                        return WheaEventKind.Informational;
                    }
                }
            }
            catch (FormatException)
            {
                // Datos dañados no son evidencia de ausencia de error.
            }
        }

        return level is >= 1 and <= 3 ? WheaEventKind.Error : WheaEventKind.Unknown;
    }
}

internal sealed class WindowsEventProvider : IReadOnlyDiagnosticProvider
{
    private const string CriticalOrErrorQuery = "*[System[(Level=1 or Level=2) and TimeCreated[timediff(@SystemTime) <= 604800000]]]";
    private const string WheaQuery = "*[System[Provider[@Name='Microsoft-Windows-WHEA-Logger'] and TimeCreated[timediff(@SystemTime) <= 604800000]]]";

    public string Id => "events";
    public string Title => "Eventos críticos y WHEA";

    public async Task<DiagnosticSection> CollectAsync(CancellationToken cancellationToken)
    {
        const string source = "Windows Event Log / wevtutil (solo lectura)";
        var executable = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "wevtutil.exe");
        var errorTask = QueryAsync(executable, CriticalOrErrorQuery, cancellationToken);
        var wheaTask = QueryAsync(executable, WheaQuery, cancellationToken);
        await Task.WhenAll(errorTask, wheaTask).ConfigureAwait(false);

        var errors = await errorTask.ConfigureAwait(false);
        var whea = await wheaTask.ConfigureAwait(false);
        var data = new List<DiagnosticDatum>
        {
            DiagnosticDatum.Available("Ventana consultada", "Últimos 7 días; máximo 50 por categoría", source, EvidenceCoverage.Partial),
        };
        AddCount(data, "Eventos críticos o de error", "events.error.count", errors, source);
        AddCount(data, "Eventos WHEA (total)", "events.whea.count", whea, source);
        AddCount(data, "Errores o advertencias WHEA", "events.whea.error.count",
            whea with { Events = whea.Events.Where(item => item.WheaKind == WheaEventKind.Error).ToArray() }, source);
        AddCount(data, "Registros WHEA informativos", "events.whea.informational.count",
            whea with { Events = whea.Events.Where(item => item.WheaKind == WheaEventKind.Informational).ToArray() }, source);
        var unclassified = whea.Events.Count(item => item.WheaKind == WheaEventKind.Unknown);
        AddCount(data, "Registros WHEA sin clasificar", "events.whea.unknown.count",
            whea with { Events = whea.Events.Where(item => item.WheaKind == WheaEventKind.Unknown).ToArray() }, source);
        if (unclassified > 0)
        {
            data.Add(DiagnosticDatum.Unknown("Clasificación WHEA", source,
                "Hay registros sin severidad interpretable; no se consideran saludables.", "events.whea.classification"));
        }

        var rank = 1;
        var recentEvents = errors.Events
            .Concat(whea.Events)
            .DistinctBy(item => (item.EventId, item.Level, item.Provider, item.CreatedAt))
            .OrderByDescending(item => item.CreatedAt)
            .Take(5);
        foreach (var item in recentEvents)
        {
            var created = item.CreatedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "fecha desconocida";
            data.Add(DiagnosticDatum.Available(
                $"Evento reciente #{rank}",
                $"{created}; {item.Provider}; ID {item.EventId.ToString(CultureInfo.InvariantCulture)}; nivel {item.Level.ToString(CultureInfo.InvariantCulture)}",
                source,
                item.CreatedAt is null ? EvidenceCoverage.Partial : EvidenceCoverage.Complete));
            rank++;
        }

        return DiagnosticSectionFactory.Create(
            Id,
            Title,
            data,
            "Errores y WHEA se consultan por separado (máximo 50). Los registros CPER informativos no penalizan salud. Un cero solo describe la muestra consultada.");
    }

    private static void AddCount(
        List<DiagnosticDatum> data,
        string name,
        string metricKey,
        EventQueryResult query,
        string source)
    {
        data.Add(query.Error is null
            ? DiagnosticDatum.Available(
                name,
                query.Events.Length.ToString(CultureInfo.InvariantCulture),
                source,
                EvidenceCoverage.Partial,
                metricKey: metricKey,
                numericValue: query.Events.Length)
            : DiagnosticDatum.Unknown(
                name,
                source,
                query.Error,
                metricKey));
    }

    private static async Task<EventQueryResult> QueryAsync(
        string executable,
        string query,
        CancellationToken cancellationToken)
    {
        string[] arguments = ["qe", "System", "/q:" + query, "/f:xml", "/c:50", "/rd:true"];
        var result = await ControlledProcessRunner.RunAsync(
            executable,
            arguments,
            TimeSpan.FromSeconds(30),
            cancellationToken).ConfigureAwait(false);
        if (result.TimedOut)
        {
            return EventQueryResult.Failed("La consulta agotó el tiempo de espera.");
        }

        if (result.ExitCode != 0)
        {
            return EventQueryResult.Failed(
                $"wevtutil terminó con código {result.ExitCode.ToString(CultureInfo.InvariantCulture)}.");
        }

        try
        {
            return EventQueryResult.Completed(WindowsEventXmlParser.Parse(result.StandardOutput));
        }
        catch (System.Xml.XmlException)
        {
            return EventQueryResult.Failed("La salida del registro de eventos no tuvo el formato esperado.");
        }
    }

    private sealed record EventQueryResult(WindowsEventRecord[] Events, string? Error)
    {
        public static EventQueryResult Completed(IReadOnlyList<WindowsEventRecord> events) => new(events.ToArray(), null);

        public static EventQueryResult Failed(string error) => new([], error);
    }
}
