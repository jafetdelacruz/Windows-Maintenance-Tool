using WinMaint.Domain;

namespace WinMaint.Domain.Tests;

public sealed class PhaseOneCapabilitiesTests
{
    [Fact]
    public void PhaseOneExposesNoRealDiagnosticsOrMutableActions()
    {
        Assert.False(PhaseOneCapabilities.HasRealDiagnostics);
        Assert.False(PhaseOneCapabilities.HasMutableActions);
    }

    [Fact]
    public void PriorityAndRiskHaveIndependentFourLevelScales()
    {
        Assert.Equal([Priority.P0, Priority.P1, Priority.P2, Priority.P3], Enum.GetValues<Priority>());
        Assert.Equal([RiskLevel.R0, RiskLevel.R1, RiskLevel.R2, RiskLevel.R3], Enum.GetValues<RiskLevel>());
    }

    [Fact]
    public void PhaseTwoEnablesOnlyReadOnlyDiagnostics()
    {
        Assert.True(PhaseTwoCapabilities.HasReadOnlyDiagnostics);
        Assert.False(PhaseTwoCapabilities.HasMutableActions);
    }

    [Fact]
    public void UnknownDatumUsesAbsentCoverageAndExplicitUnknownText()
    {
        var datum = DiagnosticDatum.Unknown("Temperatura", "Fixture", "Sensor ausente");

        Assert.Equal(DiagnosticDatum.UnknownValue, datum.Value);
        Assert.Equal(EvidenceCoverage.Absent, datum.Coverage);
        Assert.Equal("Sensor ausente", datum.Detail);
    }
}
