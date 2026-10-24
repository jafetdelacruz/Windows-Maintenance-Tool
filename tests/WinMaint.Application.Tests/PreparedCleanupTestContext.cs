using WinMaint.Domain;

namespace WinMaint.Application.Tests;

internal static class PreparedCleanupTestContext
{
    internal static TemporaryCleanupPlan Authorize(TemporaryCleanupPlan plan, DateTimeOffset now)
    {
        var session = Guid.NewGuid();
        var diagnostic = new DiagnosticReport(now,
            [new("fixture", "Fixture only", EvidenceCoverage.Partial,
                [DiagnosticDatum.Available("Compatibility", "Supported", "Fixture", metricKey: "system.compatibility", textValue: "supported"),
                 DiagnosticDatum.Available(@"Volumen C:\", "Fixture volume", "Fixture", metricKey: "storage.volume.free.percent", numericValue: 50)], [])], true);
        return plan with { SessionId = session, DiagnosticEvidence = new(session, plan.Inventory.Id, diagnostic) };
    }

    internal static IPreparedTemporaryCleanupRunner Bind(ITemporaryCleanupRunner runner, TemporaryCleanupPlan plan) => new Bound(runner, plan);

    private sealed class Bound(ITemporaryCleanupRunner runner, TemporaryCleanupPlan plan) : IPreparedTemporaryCleanupRunner
    {
        public TemporaryInventory Inventory => plan.Inventory;
        public IReadOnlyList<string> CandidateIds => plan.CandidateIds;
        public Task<TemporaryFileResult> RunAsync(string candidateId, CancellationToken cancellationToken) => runner.RunAsync(candidateId, cancellationToken);
    }
}
