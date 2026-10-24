using System.Runtime.InteropServices;
using WinMaint.Infrastructure.Win;

namespace WinMaint.Windows.Tests;

public sealed class WindowsEnvironmentTests
{
    [Fact]
    public void TestHostIsWindowsX64()
    {
        Assert.True(OperatingSystem.IsWindows());
        Assert.Equal(Architecture.X64, RuntimeInformation.OSArchitecture);
        Assert.Equal(Architecture.X64, RuntimeInformation.ProcessArchitecture);
    }

    [Fact]
    public void PhaseOneWindowsBoundaryContainsNoRealIntegration()
    {
        Assert.False(PhaseOneWindowsBoundary.HasOperatingSystemAdapters);
        Assert.False(PhaseOneWindowsBoundary.HasExternalProcessExecution);
        Assert.False(PhaseOneWindowsBoundary.HasMutableActions);
    }

    [Fact]
    public void PhaseTwoWindowsBoundaryAllowsReadOnlyIntegrationOnly()
    {
        Assert.True(PhaseTwoWindowsBoundary.HasOperatingSystemAdapters);
        Assert.True(PhaseTwoWindowsBoundary.HasReadOnlyExternalProcessExecution);
        Assert.False(PhaseTwoWindowsBoundary.HasMutableActions);
    }
}
