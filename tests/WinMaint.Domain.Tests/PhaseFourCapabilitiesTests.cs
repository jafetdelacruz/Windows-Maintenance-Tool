using WinMaint.Domain;

namespace WinMaint.Domain.Tests;

public sealed class PhaseFourCapabilitiesTests
{
    [Fact]
    public void PhaseFourEnablesPlansButKeepsMutableExecutionLocked()
    {
        Assert.True(PhaseFourCapabilities.HasVersionedActionCatalog);
        Assert.True(PhaseFourCapabilities.HasExecutablePreview);
        Assert.False(PhaseFourCapabilities.HasMutableExecutionEnabled);
        Assert.False(PhaseFourCapabilities.HasAggressiveOptimizations);
    }
}
