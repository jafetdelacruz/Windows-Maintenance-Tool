using WinMaint.Application;
using WinMaint.Domain;
using WinMaint.Infrastructure.Win;

namespace WinMaint.Windows.Tests;

public sealed class DiskOperationalEvidenceTests
{
    [Theory]
    [InlineData("OK, Predictive Failure", 2, true)]
    [InlineData("Healthy, Error", 2, true)]
    [InlineData("OK", 1, false)]
    [InlineData("Unknown", 1, false)]
    [InlineData("", 1, false)]
    public void IndividualStatesReachRulesAndUnknownCoverageIsNotNormal(string status, int count, bool adverse)
    {
        var data = new List<DiagnosticDatum>();
        var disk = new PhysicalDiskInfo("Synthetic disk", "SSD", "SATA", "Healthy", status,
            1000, null, null, null, null, null, false);
        StorageProvider.AddDisk(data, disk, 0, "synthetic source; no Windows query");
        var states = data.Where(datum => datum.MetricKey == "storage.disk.operational").ToArray();
        Assert.Equal(count, states.Length);
        Assert.DoesNotContain(states, datum => datum.TextValue?.Contains(',', StringComparison.Ordinal) == true);
        if (string.IsNullOrEmpty(status) || status == "Unknown")
            Assert.All(states, datum => Assert.Equal(EvidenceCoverage.Absent, datum.Coverage));
        var report = new DiagnosticReport(DateTimeOffset.UnixEpoch,
            [new("fixture", "Fixture", EvidenceCoverage.Partial, data, [])], true);
        Assert.Equal(adverse, HealthRuleEngine.Evaluate(report).HasPriorityZero);
        Assert.Equal(status, disk.OperationalStatus);
    }
}
