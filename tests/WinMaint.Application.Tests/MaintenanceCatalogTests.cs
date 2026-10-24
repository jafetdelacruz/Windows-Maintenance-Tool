using WinMaint.Application;
using WinMaint.Domain;

namespace WinMaint.Application.Tests;

public sealed class MaintenanceCatalogTests
{
    [Fact]
    public void EveryActionHasStableSafetyMetadataAndReportingIdentity()
    {
        var actions = MaintenanceCatalog.SafeActions;

        Assert.NotEmpty(actions);
        Assert.Equal(actions.Count, actions.Select(action => action.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(actions, action =>
        {
            Assert.Matches("^[a-z]+([.-][a-z]+)*$", action.Id);
            Assert.Equal(MaintenanceCatalog.CatalogVersion, action.Version);
            Assert.Equal(MaintenanceProfile.Safe, action.MinimumProfile);
            Assert.True(action.SupportsPreview);
            Assert.NotEmpty(action.Preconditions);
            Assert.False(string.IsNullOrWhiteSpace(action.ReversalDescription));
            Assert.True(Enum.IsDefined(action.Priority));
            Assert.True(Enum.IsDefined(action.Risk));
        });
    }

    [Fact]
    public void IrreversibleCleanupIsNeverPresentedAsLowRisk()
    {
        var irreversible = MaintenanceCatalog.SafeActions
            .Where(action => action.Reversal == ReversalKind.Irreversible);

        Assert.NotEmpty(irreversible);
        Assert.All(irreversible, action => Assert.True(action.IsMutable && action.Risk >= RiskLevel.R2));
    }

    [Fact]
    public void PlannerAddsAndOrdersRequiredDetectionSteps()
    {
        var plan = MaintenancePlanBuilder.BuildPreview(["sfc.scan-now", "volume.optimize"]);
        var ids = plan.Items.Select(item => item.Action.Id).ToArray();

        Assert.True(Array.IndexOf(ids, "dism.scan-health") < Array.IndexOf(ids, "dism.restore-health"));
        Assert.True(Array.IndexOf(ids, "dism.restore-health") < Array.IndexOf(ids, "sfc.scan-now"));
        Assert.True(Array.IndexOf(ids, "volume.analyze") < Array.IndexOf(ids, "volume.optimize"));
        Assert.All(plan.Items.SelectMany(item => item.Preconditions), result =>
            Assert.Equal(MaintenancePreconditionStatus.Unknown, result.Status));
    }

    [Fact]
    public void RepairActionsDeclarePossibleRestartWithoutCreatingRestartAction()
    {
        var repairActions = MaintenanceCatalog.SafeActions
            .Where(action => action.Id is "dism.restore-health" or "sfc.scan-now")
            .ToArray();

        Assert.All(repairActions, action => Assert.True(action.MayRequireRestart));
        Assert.DoesNotContain(MaintenanceCatalog.SafeActions, action =>
            action.Id.Contains("restart", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MutableActionCannotRunWithoutExactConfirmation()
    {
        var runner = new RecordingRunner();
        var plan = MaintenancePlanBuilder.BuildPreview(["temp.cleanup"]);
        var coordinator = new MaintenanceCoordinator(runner, mutableExecutionEnabled: true);

        var report = await coordinator.ExecuteAsync(
            plan,
            new MaintenanceAuthorization(false, new HashSet<string>()),
            CancellationToken.None);

        Assert.DoesNotContain(runner.Calls, id => id == "temp.cleanup");
        Assert.Contains(report.Actions, result =>
            result.ActionId == "temp.cleanup" && result.Outcome == MaintenanceOutcome.Blocked);
    }

    [Fact]
    public async Task ProductionLockBlocksMutationEvenWhenAuthorizationSaysApproved()
    {
        var runner = new RecordingRunner();
        var plan = MaintenancePlanBuilder.BuildPreview(["recycle.empty"]);
        var coordinator = new MaintenanceCoordinator(runner, mutableExecutionEnabled: false);

        var report = await coordinator.ExecuteAsync(
            plan,
            new MaintenanceAuthorization(true, new HashSet<string>(["recycle.empty"])),
            CancellationToken.None);

        Assert.DoesNotContain("recycle.empty", runner.Calls);
        var blocked = Assert.Single(report.Actions, result => result.ActionId == "recycle.empty");
        Assert.Equal(MaintenanceOutcome.Blocked, blocked.Outcome);
        Assert.False(blocked.KnownTemporaryArtifactsRemain);
    }

    [Fact]
    public async Task RequiredUnknownPreconditionBlocksExecution()
    {
        var runner = new RecordingRunner();
        var plan = MaintenancePlanBuilder.BuildPreview(["temp.inventory"]);
        var coordinator = new MaintenanceCoordinator(runner, mutableExecutionEnabled: false);

        var report = await coordinator.ExecuteAsync(
            plan,
            new MaintenanceAuthorization(false, new HashSet<string>()),
            CancellationToken.None);

        Assert.Empty(runner.Calls);
        Assert.Equal(MaintenanceOutcome.Blocked, Assert.Single(report.Actions).Outcome);
    }

    [Fact]
    public async Task CancelledResultStopsRemainingActions()
    {
        var runner = new CancelledRunner();
        var preview = MaintenancePlanBuilder.BuildPreview(["temp.cleanup"]);
        var executable = preview with
        {
            Items = preview.Items.Select(item => item with
            {
                Preconditions = item.Preconditions.Select(precondition => precondition with
                {
                    Status = MaintenancePreconditionStatus.Pass,
                    Detail = "Fixture aprobada.",
                }).ToArray(),
            }).ToArray(),
        };
        var coordinator = new MaintenanceCoordinator(runner, mutableExecutionEnabled: true);

        var report = await coordinator.ExecuteAsync(
            executable,
            new MaintenanceAuthorization(true, new HashSet<string>(["temp.cleanup"])),
            CancellationToken.None);

        Assert.Equal(MaintenanceOutcome.Cancelled, report.Actions[0].Outcome);
        Assert.Equal(MaintenanceOutcome.Skipped, report.Actions[1].Outcome);
        Assert.Single(runner.Calls);
    }

    private sealed class RecordingRunner : IMaintenanceActionRunner
    {
        public List<string> Calls { get; } = [];

        public Task<MaintenanceActionResult> RunAsync(
            MaintenancePlanItem item,
            CancellationToken cancellationToken)
        {
            Calls.Add(item.Action.Id);
            var now = DateTimeOffset.Now;
            return Task.FromResult(new MaintenanceActionResult(
                item.Action.Id,
                item.Action.Version,
                item.Action.Priority,
                item.Action.Risk,
                MaintenanceOutcome.Succeeded,
                now,
                now,
                0,
                0,
                0,
                false,
                false,
                "Fixture",
                "Verificado por fixture."));
        }
    }

    private sealed class CancelledRunner : IMaintenanceActionRunner
    {
        public List<string> Calls { get; } = [];

        public Task<MaintenanceActionResult> RunAsync(
            MaintenancePlanItem item,
            CancellationToken cancellationToken)
        {
            Calls.Add(item.Action.Id);
            var now = DateTimeOffset.Now;
            return Task.FromResult(new MaintenanceActionResult(
                item.Action.Id,
                item.Action.Version,
                item.Action.Priority,
                item.Action.Risk,
                MaintenanceOutcome.Cancelled,
                now,
                now,
                0,
                0,
                null,
                false,
                false,
                "Cancelada por fixture.",
                "Sin temporales conocidos."));
        }
    }
}
