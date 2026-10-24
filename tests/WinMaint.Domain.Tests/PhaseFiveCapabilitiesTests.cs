using WinMaint.Domain;

namespace WinMaint.Domain.Tests;

public sealed class PhaseFiveCapabilitiesTests
{
    [Fact]
    public void PhaseFiveEnablesOnlyTheDocumentedOfficialActions()
    {
        Assert.True(PhaseFiveCapabilities.HasReadOnlyDetection);
        Assert.False(PhaseFiveCapabilities.HasInstallationEnabled);
        Assert.True(PhaseFiveCapabilities.HasStoreFallbackEnabled);
        Assert.True(PhaseFiveCapabilities.HasLaunchEnabled);
    }
}
