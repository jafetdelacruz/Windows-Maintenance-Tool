using WinMaint.Domain;

namespace WinMaint.Application;

public sealed record DiagnosticSection(
    string Id,
    string Title,
    EvidenceCoverage Coverage,
    IReadOnlyList<DiagnosticDatum> Data,
    IReadOnlyList<string> Notes);

public sealed record DiagnosticReport(
    DateTimeOffset CapturedAt,
    IReadOnlyList<DiagnosticSection> Sections,
    bool IsReadOnly)
{
    public int UnknownValueCount => Sections.Sum(
        section => section.Data.Count(value => value.Coverage == EvidenceCoverage.Absent));
}

public interface IReadOnlyDiagnosticProvider
{
    string Id { get; }
    string Title { get; }
    Task<DiagnosticSection> CollectAsync(CancellationToken cancellationToken);
}

public sealed class ReadOnlyDiagnosticCoordinator
{
    private readonly IReadOnlyList<IReadOnlyDiagnosticProvider> providers;

    public ReadOnlyDiagnosticCoordinator(IEnumerable<IReadOnlyDiagnosticProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        this.providers = providers.ToArray();
    }

    public async Task<DiagnosticReport> CollectAsync(CancellationToken cancellationToken = default)
    {
        var tasks = providers.Select(provider => CollectSafelyAsync(provider, cancellationToken));
        var sections = await Task.WhenAll(tasks).ConfigureAwait(false);
        return new DiagnosticReport(DateTimeOffset.Now, sections, IsReadOnly: true);
    }

    private static async Task<DiagnosticSection> CollectSafelyAsync(
        IReadOnlyDiagnosticProvider provider,
        CancellationToken cancellationToken)
    {
        try
        {
            return await provider.CollectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new DiagnosticSection(
                provider.Id,
                provider.Title,
                EvidenceCoverage.Absent,
                [DiagnosticDatum.Unknown("Proveedor", provider.Id, "No se pudo recopilar esta sección.")],
                [$"Error controlado: {exception.GetType().Name}"]);
        }
    }
}
