using WinMaint.Application;
using WinMaint.Domain;

namespace WinMaint.Infrastructure.Win;

public sealed class PcManagerActionRunnerTests
{
    [Fact]
    public async Task SuccessfulWinGetExitStillRequiresInstalledPostDetection()
    {
        var gateway = new RecordingGateway
        {
            WinGetPath = @"C:\Users\Lab\AppData\Local\Microsoft\WindowsApps\winget.exe",
            ProcessResult = new ControlledProcessResult(0, "", "", TimedOut: false),
        };
        var detector = new StubDetector(Detection(
            PcManagerInstallationState.Installed,
            "Microsoft.MicrosoftPCManager_8wekyb3d8bbwe!App"));
        var runner = new WindowsPcManagerActionRunner(detector, gateway);

        var result = await runner.RunAsync(
            Plan(PcManagerProposedAction.InstallFromMicrosoftStoreWithWinGet),
            CancellationToken.None);

        Assert.Equal(PcManagerActionOutcome.Succeeded, result.Outcome);
        Assert.Equal(0, result.ExitCode);
        Assert.NotNull(gateway.Command);
        Assert.Contains("9PM860492SZD", gateway.Command.Arguments);
        Assert.Contains("msstore", gateway.Command.Arguments);
    }

    [Fact]
    public async Task ZeroExitWithoutInstalledPostDetectionIsFailure()
    {
        var gateway = new RecordingGateway
        {
            WinGetPath = @"C:\Users\Lab\AppData\Local\Microsoft\WindowsApps\winget.exe",
            ProcessResult = new ControlledProcessResult(0, "", "", TimedOut: false),
        };
        var runner = new WindowsPcManagerActionRunner(
            new StubDetector(Detection(PcManagerInstallationState.Absent, null)),
            gateway);

        var result = await runner.RunAsync(
            Plan(PcManagerProposedAction.InstallFromMicrosoftStoreWithWinGet),
            CancellationToken.None);

        Assert.Equal(PcManagerActionOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task StoreFallbackUsesOnlyOfficialProductUri()
    {
        var gateway = new RecordingGateway();
        var runner = new WindowsPcManagerActionRunner(
            new StubDetector(Detection(PcManagerInstallationState.Absent, null)),
            gateway);

        var result = await runner.RunAsync(
            Plan(PcManagerProposedAction.OpenOfficialStorePage),
            CancellationToken.None);

        Assert.Equal(PcManagerActionOutcome.Succeeded, result.Outcome);
        Assert.Equal(PcManagerCommandFactory.OfficialStoreUri, gateway.StoreUri?.AbsoluteUri);
    }

    [Fact]
    public async Task LaunchRejectsUntrustedApplicationIdentity()
    {
        var gateway = new RecordingGateway();
        var runner = new WindowsPcManagerActionRunner(
            new StubDetector(Detection(
                PcManagerInstallationState.Installed,
                "Microsoft.PCManager!App & calc.exe")),
            gateway);

        var result = await runner.RunAsync(
            Plan(PcManagerProposedAction.LaunchInstalled),
            CancellationToken.None);

        Assert.Equal(PcManagerActionOutcome.Failed, result.Outcome);
        Assert.Null(gateway.LaunchedApplicationUserModelId);
    }

    [Fact]
    public async Task LaunchUsesValidatedInstalledApplicationIdentity()
    {
        const string applicationUserModelId = "Microsoft.MicrosoftPCManager_8wekyb3d8bbwe!App";
        var gateway = new RecordingGateway();
        var runner = new WindowsPcManagerActionRunner(
            new StubDetector(Detection(PcManagerInstallationState.Installed, applicationUserModelId)),
            gateway);

        var result = await runner.RunAsync(
            Plan(PcManagerProposedAction.LaunchInstalled),
            CancellationToken.None);

        Assert.Equal(PcManagerActionOutcome.Succeeded, result.Outcome);
        Assert.Equal(applicationUserModelId, gateway.LaunchedApplicationUserModelId);
    }

    [Theory]
    [InlineData(PcManagerProposedAction.OpenOfficialStorePage)]
    [InlineData(PcManagerProposedAction.LaunchInstalled)]
    public async Task CancelledRunnerNeverOpensExternalApplication(PcManagerProposedAction action)
    {
        var gateway = new RecordingGateway();
        var runner = new WindowsPcManagerActionRunner(new StubDetector(Detection(PcManagerInstallationState.Installed,
            "Microsoft.MicrosoftPCManager_8wekyb3d8bbwe!App")), gateway);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(Plan(action), cancelled.Token));
        Assert.Null(gateway.StoreUri);
        Assert.Null(gateway.LaunchedApplicationUserModelId);
    }

    [Fact]
    public async Task CancellationDuringDetectionIsCheckedAgainBeforeLaunch()
    {
        var gateway = new RecordingGateway();
        using var cancelled = new CancellationTokenSource();
        var detector = new StubDetector(Detection(PcManagerInstallationState.Installed,
            "Microsoft.MicrosoftPCManager_8wekyb3d8bbwe!App"), cancelled.Cancel);
        var runner = new WindowsPcManagerActionRunner(detector, gateway);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            runner.RunAsync(Plan(PcManagerProposedAction.LaunchInstalled), cancelled.Token));
        Assert.Null(gateway.LaunchedApplicationUserModelId);
    }

    private static PcManagerActionPlan Plan(PcManagerProposedAction action) => new(
        "1.0",
        DateTimeOffset.UnixEpoch,
        action,
        Priority.P3,
        action == PcManagerProposedAction.InstallFromMicrosoftStoreWithWinGet ? RiskLevel.R1 : RiskLevel.R0,
        IsMutable: action == PcManagerProposedAction.InstallFromMicrosoftStoreWithWinGet,
        RequiresExplicitAuthorization: true,
        RequiresNetwork: action != PcManagerProposedAction.LaunchInstalled,
        MaintenanceResource.Store,
        "Fixture.");

    private static PcManagerDetectionResult Detection(
        PcManagerInstallationState installation,
        string? applicationUserModelId) => new(
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
                    applicationUserModelId)
                : null,
            AvailabilityState.Available,
            AvailabilityState.Available,
            StorePolicyState.NotBlockedByKnownPolicy,
            AvailabilityState.Available,
            []);

    private sealed class StubDetector(PcManagerDetectionResult result, Action? onDetect = null) : IPcManagerDetector
    {
        public Task<PcManagerDetectionResult> DetectAsync(CancellationToken cancellationToken)
        {
            onDetect?.Invoke();
            return Task.FromResult(result);
        }
    }

    private sealed class RecordingGateway : IPcManagerPlatformGateway
    {
        public string? WinGetPath { get; init; }
        public ControlledProcessResult ProcessResult { get; init; } =
            new(0, "", "", TimedOut: false);
        public MaintenanceCommand? Command { get; private set; }
        public Uri? StoreUri { get; private set; }
        public string? LaunchedApplicationUserModelId { get; private set; }

        public string? ResolveWinGetPath() => WinGetPath;

        public Task<ControlledProcessResult> RunProcessAsync(
            MaintenanceCommand command,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            Command = command;
            return Task.FromResult(ProcessResult);
        }

        public bool OpenStoreProductPage(Uri uri)
        {
            StoreUri = uri;
            return true;
        }

        public bool LaunchApplication(string applicationUserModelId)
        {
            LaunchedApplicationUserModelId = applicationUserModelId;
            return true;
        }
    }
}
