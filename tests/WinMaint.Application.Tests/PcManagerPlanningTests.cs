using WinMaint.Application;
using WinMaint.Domain;

namespace WinMaint.Application.Tests;

public sealed class PcManagerPlanningTests
{
    [Fact]
    public void InstalledPackageOnlyProposesLaunch()
    {
        var plan = PcManagerPlanBuilder.Build(Detection(
            PcManagerInstallationState.Installed,
            AvailabilityState.Available,
            AvailabilityState.Available,
            StorePolicyState.NotBlockedByKnownPolicy,
            AvailabilityState.Available));

        Assert.Equal(PcManagerProposedAction.LaunchInstalled, plan.Action);
        Assert.False(plan.IsMutable);
        Assert.True(plan.RequiresExplicitAuthorization);
    }

    [Theory]
    [InlineData(AvailabilityState.Available)]
    [InlineData(AvailabilityState.Unavailable)]
    [InlineData(AvailabilityState.Unknown)]
    public void AbsentPackageOnlyProposesManualStoreInstallation(AvailabilityState winget)
    {
        var plan = PcManagerPlanBuilder.Build(Detection(
            PcManagerInstallationState.Absent,
            winget,
            AvailabilityState.Available,
            StorePolicyState.NotBlockedByKnownPolicy,
            AvailabilityState.Available));

        Assert.Equal(PcManagerProposedAction.OpenOfficialStorePage, plan.Action);
        Assert.False(plan.IsMutable);
        Assert.Equal(RiskLevel.R0, plan.Risk);
        Assert.True(plan.Resources.HasFlag(MaintenanceResource.Store));
        Assert.True(plan.Resources.HasFlag(MaintenanceResource.Network));
    }

    [Fact]
    public void MissingWinGetFallsBackToOfficialStorePage()
    {
        var plan = PcManagerPlanBuilder.Build(Detection(
            PcManagerInstallationState.Absent,
            AvailabilityState.Unavailable,
            AvailabilityState.Available,
            StorePolicyState.NotBlockedByKnownPolicy,
            AvailabilityState.Available));

        Assert.Equal(PcManagerProposedAction.OpenOfficialStorePage, plan.Action);
        Assert.False(plan.IsMutable);
        Assert.True(plan.RequiresExplicitAuthorization);
    }

    [Theory]
    [InlineData(StorePolicyState.Blocked, AvailabilityState.Available)]
    [InlineData(StorePolicyState.Unknown, AvailabilityState.Available)]
    [InlineData(StorePolicyState.NotBlockedByKnownPolicy, AvailabilityState.Unavailable)]
    [InlineData(StorePolicyState.NotBlockedByKnownPolicy, AvailabilityState.Unknown)]
    public void StorePolicyOrNetworkUncertaintyBlocksAction(
        StorePolicyState storePolicy,
        AvailabilityState network)
    {
        var plan = PcManagerPlanBuilder.Build(Detection(
            PcManagerInstallationState.Absent,
            AvailabilityState.Available,
            AvailabilityState.Available,
            storePolicy,
            network));

        Assert.True(plan.IsBlocked);
        Assert.Equal(PcManagerProposedAction.None, plan.Action);
    }

    [Fact]
    public void UnknownInstallationNeverBecomesInstallRecommendation()
    {
        var plan = PcManagerPlanBuilder.Build(Detection(
            PcManagerInstallationState.Unknown,
            AvailabilityState.Available,
            AvailabilityState.Available,
            StorePolicyState.NotBlockedByKnownPolicy,
            AvailabilityState.Available));

        Assert.True(plan.IsBlocked);
    }

    private static PcManagerDetectionResult Detection(
        PcManagerInstallationState installation,
        AvailabilityState winget,
        AvailabilityState store,
        StorePolicyState storePolicy,
        AvailabilityState network) => new(
            "1.0",
            DateTimeOffset.UnixEpoch,
            26200,
            IsX64OperatingSystem: true,
            AvailabilityState.Available,
            installation,
            installation == PcManagerInstallationState.Installed
                ? new PcManagerPackageInfo(
                    "Microsoft.MicrosoftPCManager",
                    "3.20.1.0",
                    "Microsoft.MicrosoftPCManager_8wekyb3d8bbwe",
                    "Microsoft.MicrosoftPCManager_8wekyb3d8bbwe!App")
                : null,
            winget,
            store,
            storePolicy,
            network,
            []);
}
