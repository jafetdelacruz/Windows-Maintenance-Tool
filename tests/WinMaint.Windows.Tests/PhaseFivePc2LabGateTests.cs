using WinMaint.Infrastructure.Win;

namespace WinMaint.Windows.Tests;

public sealed class PhaseFivePc2LabGateTests
{
    private const string LocalAppData = @"C:\Users\LabUser\AppData\Local";
    private static readonly string ExecutablePath = Path.Combine(
        LocalAppData,
        WindowsMaintenanceComposition.PhaseFivePc2LabFolderName,
        "WinMaint-Phase5-PC2-Lab.exe");

    [Fact]
    public void AcceptsOnlyTheExactWindows10X64LabEnvironment()
    {
        var accepted = WindowsMaintenanceComposition.MeetsPhaseFivePc2LabRequirements(
            isWindows: true,
            operatingSystemBuild: 19045,
            is64BitOperatingSystem: true,
            ExecutablePath,
            LocalAppData,
            WindowsMaintenanceComposition.PhaseFivePc2LabMarkerText);

        Assert.True(accepted);
    }

    [Theory]
    [InlineData(false, 19045, true, "WINMAINT_PHASE5_PC2_RECOVERABLE_LAB")]
    [InlineData(true, 26200, true, "WINMAINT_PHASE5_PC2_RECOVERABLE_LAB")]
    [InlineData(true, 19045, false, "WINMAINT_PHASE5_PC2_RECOVERABLE_LAB")]
    [InlineData(true, 19045, true, "wrong")]
    public void RejectsAnyMismatchedEnvironment(
        bool isWindows,
        int build,
        bool is64Bit,
        string marker)
    {
        var accepted = WindowsMaintenanceComposition.MeetsPhaseFivePc2LabRequirements(
            isWindows,
            build,
            is64Bit,
            ExecutablePath,
            LocalAppData,
            marker);

        Assert.False(accepted);
    }

    [Fact]
    public void RejectsExecutableOutsideTheDedicatedFolder()
    {
        var accepted = WindowsMaintenanceComposition.MeetsPhaseFivePc2LabRequirements(
            isWindows: true,
            operatingSystemBuild: 19045,
            is64BitOperatingSystem: true,
            @"C:\Temp\WinMaint-Phase5-PC2-Lab.exe",
            LocalAppData,
            WindowsMaintenanceComposition.PhaseFivePc2LabMarkerText);

        Assert.False(accepted);
    }
}
