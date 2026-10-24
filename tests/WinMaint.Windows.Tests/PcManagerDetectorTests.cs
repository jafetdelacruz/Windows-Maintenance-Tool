using WinMaint.Domain;

namespace WinMaint.Infrastructure.Win;

public sealed class PcManagerDetectorTests
{
    [Fact]
    public void InstalledFixturePreservesPackageIdentityAndAvailableChannels()
    {
        var probe = PcManagerProbeParser.Parse(ReadFixture("pcmanager-installed.json"));

        var result = PcManagerDetectionMapper.Map(
            probe,
            operatingSystemBuild: 26200,
            isWindows: true,
            isX64OperatingSystem: true,
            StorePolicyState.NotBlockedByKnownPolicy,
            networkInterfaceAvailable: true,
            new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal(PcManagerInstallationState.Installed, result.Installation);
        Assert.Equal("Microsoft.MicrosoftPCManager", result.Package?.Name);
        Assert.Equal("3.20.1.0", result.Package?.Version);
        Assert.Equal(AvailabilityState.Available, result.SystemCompatibility);
        Assert.Equal(AvailabilityState.Available, result.WinGet);
        Assert.Equal(AvailabilityState.Available, result.MicrosoftStorePackage);
    }

    [Fact]
    public void AbsentAndBlockedFixtureDoesNotBecomeUnknownOrHealthy()
    {
        var probe = PcManagerProbeParser.Parse(ReadFixture("pcmanager-absent-store-blocked.json"));

        var result = PcManagerDetectionMapper.Map(
            probe,
            operatingSystemBuild: 19045,
            isWindows: true,
            isX64OperatingSystem: true,
            StorePolicyState.Blocked,
            networkInterfaceAvailable: false,
            DateTimeOffset.UnixEpoch);

        Assert.Equal(PcManagerInstallationState.Absent, result.Installation);
        Assert.Null(result.Package);
        Assert.Equal(AvailabilityState.Unavailable, result.WinGet);
        Assert.Equal(AvailabilityState.Unavailable, result.MicrosoftStorePackage);
        Assert.Equal(StorePolicyState.Blocked, result.MicrosoftStorePolicy);
        Assert.Equal(AvailabilityState.Unavailable, result.NetworkInterface);
    }

    [Fact]
    public void FailedProbePreservesUnknownStates()
    {
        var result = PcManagerDetectionMapper.Map(
            probe: null,
            operatingSystemBuild: 26200,
            isWindows: true,
            isX64OperatingSystem: true,
            StorePolicyState.Unknown,
            networkInterfaceAvailable: null,
            DateTimeOffset.UnixEpoch);

        Assert.Equal(PcManagerInstallationState.Unknown, result.Installation);
        Assert.Equal(AvailabilityState.Unknown, result.WinGet);
        Assert.Equal(AvailabilityState.Unknown, result.MicrosoftStorePackage);
        Assert.Equal(StorePolicyState.Unknown, result.MicrosoftStorePolicy);
        Assert.Equal(AvailabilityState.Unknown, result.NetworkInterface);
    }

    [Theory]
    [InlineData(19041, true, true, AvailabilityState.Unavailable)]
    [InlineData(19042, true, true, AvailabilityState.Available)]
    [InlineData(26200, true, true, AvailabilityState.Available)]
    [InlineData(26200, true, false, AvailabilityState.Unavailable)]
    public void CompatibilityRequiresWindowsBuild19042OrLaterAndX64(
        int build,
        bool isWindows,
        bool isX64,
        AvailabilityState expected)
    {
        var result = PcManagerDetectionMapper.Map(
            probe: null,
            build,
            isWindows,
            isX64,
            StorePolicyState.NotBlockedByKnownPolicy,
            networkInterfaceAvailable: true,
            DateTimeOffset.UnixEpoch);

        Assert.Equal(expected, result.SystemCompatibility);
    }

    private static string ReadFixture(string name) => File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}
