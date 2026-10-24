using WinMaint.Application;
using WinMaint.Domain;

namespace WinMaint.Application.Tests;

public sealed class ReadOnlyDiagnosticCoordinatorTests
{
    [Fact]
    public async Task CollectAsyncPreservesUnknownDataWithoutCallingItHealthy()
    {
        var coordinator = new ReadOnlyDiagnosticCoordinator([new UnknownProvider()]);

        var report = await coordinator.CollectAsync(CancellationToken.None);

        Assert.True(report.IsReadOnly);
        Assert.Equal(1, report.UnknownValueCount);
        var datum = Assert.Single(Assert.Single(report.Sections).Data);
        Assert.Equal(DiagnosticDatum.UnknownValue, datum.Value);
        Assert.Equal(EvidenceCoverage.Absent, datum.Coverage);
    }

    [Fact]
    public async Task CollectAsyncConvertsProviderFailureIntoAbsentEvidence()
    {
        var coordinator = new ReadOnlyDiagnosticCoordinator([new FailingProvider()]);

        var report = await coordinator.CollectAsync(CancellationToken.None);

        var section = Assert.Single(report.Sections);
        Assert.Equal(EvidenceCoverage.Absent, section.Coverage);
        Assert.Equal(DiagnosticDatum.UnknownValue, Assert.Single(section.Data).Value);
    }

    private sealed class UnknownProvider : IReadOnlyDiagnosticProvider
    {
        public string Id => "unknown";
        public string Title => "Desconocido";

        public Task<DiagnosticSection> CollectAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new DiagnosticSection(
                Id,
                Title,
                EvidenceCoverage.Absent,
                [DiagnosticDatum.Unknown("Dato", Id, "Sin fuente")],
                []));
    }

    private sealed class FailingProvider : IReadOnlyDiagnosticProvider
    {
        public string Id => "failure";
        public string Title => "Falla";

        public Task<DiagnosticSection> CollectAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Falla de fixture");
    }
}
