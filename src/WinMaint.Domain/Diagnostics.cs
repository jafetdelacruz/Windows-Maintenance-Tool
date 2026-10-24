namespace WinMaint.Domain;

public enum EvidenceCoverage
{
    Complete,
    Partial,
    Absent,
}

public sealed record DiagnosticDatum(
    string Name,
    string Value,
    string Source,
    EvidenceCoverage Coverage,
    string? Detail = null,
    string? MetricKey = null,
    double? NumericValue = null,
    string? TextValue = null)
{
    public const string UnknownValue = "Desconocido";

    public static DiagnosticDatum Available(
        string name,
        string value,
        string source,
        EvidenceCoverage coverage = EvidenceCoverage.Complete,
        string? detail = null,
        string? metricKey = null,
        double? numericValue = null,
        string? textValue = null) =>
        new(name, value, source, coverage, detail, metricKey, numericValue, textValue);

    public static DiagnosticDatum Unknown(
        string name,
        string source,
        string reason,
        string? metricKey = null) =>
        new(name, UnknownValue, source, EvidenceCoverage.Absent, reason, metricKey);
}

public static class PhaseTwoCapabilities
{
    public const bool HasReadOnlyDiagnostics = true;
    public const bool HasMutableActions = false;
}
