using WinMaint.Domain;

namespace WinMaint.Domain.Tests;

public sealed class PhaseThreeCapabilitiesTests
{
    [Fact]
    public void PhaseThreeAddsAssessmentAndControlledTestButNoMaintenance()
    {
        Assert.True(PhaseThreeCapabilities.HasVersionedHealthRules);
        Assert.True(PhaseThreeCapabilities.HasControlledStabilityTest);
        Assert.False(PhaseThreeCapabilities.HasMaintenanceActions);
    }
}
