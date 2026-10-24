using System.Text.Json;
using WinMaint.Application;
using WinMaint.Reporting;

namespace WinMaint.Application.Tests;

public sealed class LaunchHardeningTests
{
    [Theory]
    [InlineData("--report-dir")]
    [InlineData("--report-summary")]
    [InlineData("--unknown")]
    [InlineData("--confirm-work-saved")]
    public void InvalidArgumentsAreRejectedInsteadOfOpeningMenu(string argument) =>
        Assert.Throws<ArgumentException>(() => CliOptions.Parse([argument]));

    [Fact]
    public void PathValuesAreNotParsedAsModes()
    {
        var options = CliOptions.Parse(["--report", "--report-dir", @"C:\Reports\--quick-test", "--no-ansi"]);
        Assert.True(options.RunReport);
        Assert.False(options.RunStability);
        Assert.Equal(@"C:\Reports\--quick-test", options.ReportDirectory);
    }

    [Fact]
    public void ConflictingModesAndRepeatedPathsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => CliOptions.Parse(["--report", "--quick-test"]));
        Assert.Throws<ArgumentException>(() => CliOptions.Parse(["--report-dir", "first", "--report-dir", "second"]));
        Assert.Throws<ArgumentException>(() => CliOptions.Parse(["--report-dir", "--quick-test"]));
    }

    [Theory]
    [InlineData(true, true, 19045, true)]
    [InlineData(true, true, 26200, true)]
    [InlineData(true, true, 19042, false)]
    [InlineData(true, false, 26200, false)]
    [InlineData(false, true, 26200, false)]
    public void PlatformGateRequiresTargetWindowsX64(bool windows, bool x64, int build, bool accepted) =>
        Assert.Equal(accepted, LaunchPolicy.SupportsPlatform(windows, x64, build));

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":\"2.0\"}")]
    [InlineData("not json")]
    public void InvalidReportsAreRejectedWithControlledJsonError(string json) =>
        Assert.Throws<JsonException>(() => ReportRenderer.Parse(json));
}
