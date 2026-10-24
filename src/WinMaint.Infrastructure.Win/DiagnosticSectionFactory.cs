using WinMaint.Application;
using WinMaint.Domain;

namespace WinMaint.Infrastructure.Win;

internal static class DiagnosticSectionFactory
{
    internal static DiagnosticSection Create(
        string id,
        string title,
        IReadOnlyList<DiagnosticDatum> data,
        params string[] notes)
    {
        var present = data.Count(value => value.Coverage != EvidenceCoverage.Absent);
        var coverage = present switch
        {
            0 => EvidenceCoverage.Absent,
            _ when present == data.Count && data.All(value => value.Coverage == EvidenceCoverage.Complete) => EvidenceCoverage.Complete,
            _ => EvidenceCoverage.Partial,
        };

        return new DiagnosticSection(id, title, coverage, data, notes);
    }
}
