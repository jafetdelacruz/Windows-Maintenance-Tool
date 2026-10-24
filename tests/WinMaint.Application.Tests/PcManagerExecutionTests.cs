using WinMaint.Application;
using WinMaint.Domain;

namespace WinMaint.Application.Tests;

public sealed class PcManagerExecutionTests
{
    [Theory]
    [InlineData(PcManagerProposedAction.InstallFromMicrosoftStoreWithWinGet, "pc-manager.install")]
    [InlineData(PcManagerProposedAction.OpenOfficialStorePage, "pc-manager.store")]
    [InlineData(PcManagerProposedAction.LaunchInstalled, "pc-manager.launch")]
    public void ExactTokenAuthorizesOnlyItsAction(
        PcManagerProposedAction action,
        string token)
    {
        var authorization = PcManagerAuthorizationPolicy.Evaluate(action, token);

        Assert.True(authorization.Approved);
        Assert.Equal(action, authorization.ApprovedAction);
        Assert.False(PcManagerAuthorizationPolicy.Evaluate(action, token.ToUpperInvariant()).Approved);
        Assert.False(PcManagerAuthorizationPolicy.Evaluate(action, token + " ").Approved);
        Assert.False(PcManagerAuthorizationPolicy.Evaluate(action, null).Approved);
    }

    [Fact]
    public async Task MissingConsentCancelsWithoutCallingRunner()
    {
        var runner = new RecordingRunner();
        var plan = Plan(PcManagerProposedAction.InstallFromMicrosoftStoreWithWinGet, mutable: true);
        var coordinator = new PcManagerActionCoordinator(
            runner,
            installExecutionEnabled: true,
            externalLaunchEnabled: true);

        var result = await coordinator.ExecuteAsync(
            plan,
            new PcManagerAuthorization(false, PcManagerProposedAction.None),
            CancellationToken.None);

        Assert.Equal(PcManagerActionOutcome.Cancelled, result.Outcome);
        Assert.Empty(runner.Calls);
        Assert.Null(result.ExitCode);
    }

    [Fact]
    public async Task ConsentForDifferentActionDoesNotAuthorizeInstall()
    {
        var runner = new RecordingRunner();
        var plan = Plan(PcManagerProposedAction.InstallFromMicrosoftStoreWithWinGet, mutable: true);
        var coordinator = new PcManagerActionCoordinator(
            runner,
            installExecutionEnabled: true,
            externalLaunchEnabled: true);

        var result = await coordinator.ExecuteAsync(
            plan,
            new PcManagerAuthorization(true, PcManagerProposedAction.OpenOfficialStorePage),
            CancellationToken.None);

        Assert.Equal(PcManagerActionOutcome.Cancelled, result.Outcome);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task ProductionLockWinsEvenWithExactInstallConsent()
    {
        var runner = new RecordingRunner();
        var plan = Plan(PcManagerProposedAction.InstallFromMicrosoftStoreWithWinGet, mutable: true);
        var coordinator = new PcManagerActionCoordinator(
            runner,
            installExecutionEnabled: false,
            externalLaunchEnabled: false);

        var result = await coordinator.ExecuteAsync(
            plan,
            new PcManagerAuthorization(true, plan.Action),
            CancellationToken.None);

        Assert.Equal(PcManagerActionOutcome.Blocked, result.Outcome);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task BlockedPlanNeverCallsRunner()
    {
        var runner = new RecordingRunner();
        var plan = Plan(PcManagerProposedAction.None, mutable: false);
        var coordinator = new PcManagerActionCoordinator(
            runner,
            installExecutionEnabled: true,
            externalLaunchEnabled: true);

        var result = await coordinator.ExecuteAsync(
            plan,
            new PcManagerAuthorization(true, PcManagerProposedAction.None),
            CancellationToken.None);

        Assert.Equal(PcManagerActionOutcome.Blocked, result.Outcome);
        Assert.Empty(runner.Calls);
    }

    [Theory]
    [InlineData(PcManagerProposedAction.OpenOfficialStorePage)]
    [InlineData(PcManagerProposedAction.LaunchInstalled)]
    public async Task AlreadyCancelledTokenNeverInvokesRunner(PcManagerProposedAction action)
    {
        var runner = new RecordingRunner();
        var coordinator = new PcManagerActionCoordinator(runner, false, true);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var result = await coordinator.ExecuteAsync(Plan(action, false), new(true, action), cancelled.Token);
        Assert.Equal(PcManagerActionOutcome.Cancelled, result.Outcome);
        Assert.Empty(runner.Calls);
    }

    private static PcManagerActionPlan Plan(PcManagerProposedAction action, bool mutable) => new(
        "1.0",
        DateTimeOffset.UnixEpoch,
        action,
        Priority.P3,
        mutable ? RiskLevel.R1 : RiskLevel.R0,
        mutable,
        RequiresExplicitAuthorization: action != PcManagerProposedAction.None,
        RequiresNetwork: mutable,
        mutable ? MaintenanceResource.Store | MaintenanceResource.Network : MaintenanceResource.None,
        action == PcManagerProposedAction.None ? "Fixture bloqueada." : "Fixture ejecutable.");

    private sealed class RecordingRunner : IPcManagerActionRunner
    {
        public List<PcManagerProposedAction> Calls { get; } = [];

        public Task<PcManagerActionResult> RunAsync(
            PcManagerActionPlan plan,
            CancellationToken cancellationToken)
        {
            Calls.Add(plan.Action);
            return Task.FromResult(new PcManagerActionResult(
                PcManagerActionCoordinator.ResultSchemaVersion,
                plan.Action,
                PcManagerActionOutcome.Succeeded,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch,
                0,
                "Fixture.",
                "Fixture."));
        }
    }
}
