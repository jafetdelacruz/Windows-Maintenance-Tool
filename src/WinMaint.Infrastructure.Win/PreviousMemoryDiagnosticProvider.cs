using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using WinMaint.Application;
using WinMaint.Domain;

namespace WinMaint.Infrastructure.Win;

internal sealed class PreviousMemoryDiagnosticProvider : IReadOnlyDiagnosticProvider
{
    private const string Provider = "Microsoft-Windows-MemoryDiagnostics-Results";
    private const string Source = "Windows Event Log / MemoryDiagnostics-Results (solo lectura)";
    public string Id => "memory-diagnostic-previous";
    public string Title => "Prueba previa de memoria";

    public async Task<DiagnosticSection> CollectAsync(CancellationToken cancellationToken)
    {
        var result = await ControlledProcessRunner.RunAsync(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wevtutil.exe"),
            ["qe", "System", "/q:*[System[Provider[@Name='" + Provider + "']]]", "/f:xml", "/c:1", "/rd:true"],
            TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
        var datum = result.TimedOut || result.ExitCode != 0
            ? Unknown("La consulta no terminó correctamente; no se interpreta como prueba aprobada.")
            : Parse(result.StandardOutput, DateTimeOffset.UtcNow);
        return DiagnosticSectionFactory.Create(Id, Title, [datum]);
    }

    internal static DiagnosticDatum Parse(string xml, DateTimeOffset asOf)
    {
        if (string.IsNullOrWhiteSpace(xml)) return Unknown("No se encontró un resultado previo en el registro consultado.");
        try
        {
            using var text = new StringReader(xml);
            using var reader = XmlReader.Create(text, new XmlReaderSettings
            { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 });
            var document = XDocument.Load(reader);
            XNamespace ns = "http://schemas.microsoft.com/win/2004/08/events/event";
            var events = document.Descendants(ns + "Event").ToArray();
            if (events.Length != 1) return Unknown("El resultado no contiene exactamente un evento interpretable.");
            var system = events[0].Element(ns + "System");
            if (system?.Element(ns + "Provider")?.Attribute("Name")?.Value != Provider ||
                !int.TryParse(system.Element(ns + "EventID")?.Value, CultureInfo.InvariantCulture, out var id) ||
                !int.TryParse(system.Element(ns + "Level")?.Value, CultureInfo.InvariantCulture, out var level) ||
                !DateTimeOffset.TryParse(system.Element(ns + "TimeCreated")?.Attribute("SystemTime")?.Value,
                    CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var createdAt) ||
                createdAt > asOf || createdAt == default)
                return Unknown("Proveedor, fecha o cabecera no interpretable; no se presume éxito.");
            var status = (id, level) switch
            {
                (1101 or 1201, 4) => "passed",
                (1102 or 1202, 2) => "failed",
                _ => "unknown",
            };
            // Avoid classifying a contradictory payload as a successful test.
            var badPages = events[0].Descendants().Where(element => element.Name.LocalName == "NumBadPages").ToArray();
            if (badPages.Length > 1 || badPages.Length == 1 &&
                (!long.TryParse(badPages[0].Value, CultureInfo.InvariantCulture, out var pages) || pages < 0 ||
                 status == "passed" && pages > 0)) status = "unknown";
            if (status == "unknown") return Unknown("El último evento es cancelación, prueba incompleta, desconocido o contradictorio.");
            var value = status == "passed" ? "Sin errores detectados en la prueba previa" : "Errores detectados en la prueba previa";
            return DiagnosticDatum.Available("Windows Memory Diagnostic previo", value, Source, EvidenceCoverage.Partial,
                $"Evento {id}, {createdAt:O}; resultado histórico, no certifica toda la RAM ni su estado actual.",
                "memory.diagnostic.previous.status", textValue: status);
        }
        catch (XmlException) { return Unknown("El resultado de eventos no tiene el formato esperado."); }
    }

    private static DiagnosticDatum Unknown(string reason) => DiagnosticDatum.Unknown(
        "Windows Memory Diagnostic previo", Source, reason, "memory.diagnostic.previous.status");
}
