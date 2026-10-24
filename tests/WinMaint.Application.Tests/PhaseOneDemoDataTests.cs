using WinMaint.Application;
using WinMaint.Domain;

namespace WinMaint.Application.Tests;

public sealed class PhaseFiveUiDataTests
{
    [Fact]
    public void DashboardDoesNotPresentUncollectedValuesAsRealData()
    {
        var dashboard = PhaseFiveUiData.CreateDashboard();

        Assert.False(dashboard.IsSimulated);
        Assert.Contains("REPORTES LOCALES", dashboard.Banner, StringComparison.Ordinal);
        Assert.Contains("diagnóstico", dashboard.HealthStatus, StringComparison.Ordinal);
        Assert.Contains("Pendiente", dashboard.ConfidenceStatus, StringComparison.Ordinal);
        Assert.Contains(dashboard.SystemSummary, value => value.Origin == PhaseFiveUiData.PendingOrigin);
        Assert.Empty(dashboard.Findings);
    }

    [Fact]
    public void MenuMatchesTheTenProductEntriesAndPhaseFiveDetectionIsImplemented()
    {
        var menu = PhaseFiveUiData.CreateMainMenu();

        Assert.Equal(10, menu.Count);
        Assert.Equal("diagnostic", menu[0].Id);
        Assert.Equal("exit", menu[^1].Id);
        Assert.Equal(8, menu.Count(option => option.IsImplemented));
        Assert.Contains(menu, option => option.Id == "diagnostic" && option.IsImplemented);
        Assert.Contains(menu, option => option.Id == "stability" && option.IsImplemented);
        Assert.Contains(menu, option => option.Id == "maintenance" && option.IsImplemented);
        Assert.Contains(menu, option => option.Id == "cleanup" && option.IsImplemented);
        Assert.Contains(menu, option => option.Id == "integrity" && option.IsImplemented);
        Assert.Contains(menu, option => option.Id == "pc-manager" && option.IsImplemented);
        Assert.Contains(menu, option => option.Id == "exit" && option.IsImplemented);
    }

    [Fact]
    public void MultipleSelectionChoicesComeFromVersionedSafeCatalog()
    {
        var choices = PhaseFiveUiData.CreateMaintenanceChoices();

        Assert.True(choices.Count > 1);
        Assert.All(choices, choice => Assert.Equal(MaintenanceProfile.Safe, choice.MinimumProfile));
        Assert.All(choices, choice => Assert.True(choice.SupportsPreview));
    }
}
