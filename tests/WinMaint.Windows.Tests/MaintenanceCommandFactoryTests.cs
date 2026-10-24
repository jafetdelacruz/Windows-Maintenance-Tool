namespace WinMaint.Infrastructure.Win;

public sealed class MaintenanceCommandFactoryTests
{
    [Fact]
    public void ChkdskScanNeverIncludesRepairFlags()
    {
        var command = MaintenanceCommandFactory.ChkdskScan("c:");

        Assert.True(Path.IsPathFullyQualified(command.ExecutablePath));
        Assert.Equal(["C:", "/scan"], command.Arguments);
        Assert.DoesNotContain(command.Arguments, value =>
            value is "/f" or "/r" or "/x" or "/b");
    }

    [Fact]
    public void OptimizeDelegatesMediaChoiceToWindows()
    {
        var command = MaintenanceCommandFactory.OptimizeVolume("d");

        Assert.Equal(["D:", "/O", "/U", "/V"], command.Arguments);
        Assert.DoesNotContain("/D", command.Arguments);
    }

    [Fact]
    public void DismRepairAndSfcUseSeparateArgumentTokens()
    {
        Assert.Equal(["/Online", "/Cleanup-Image", "/RestoreHealth"],
            MaintenanceCommandFactory.DismRestoreHealth().Arguments);
        Assert.Equal(["/scannow"], MaintenanceCommandFactory.SfcScanNow().Arguments);
    }

    [Theory]
    [InlineData("C & format D")]
    [InlineData("C;")]
    [InlineData("CC")]
    public void DriveLetterRejectsCommandText(string input)
    {
        Assert.Throws<ArgumentException>(() => MaintenanceCommandFactory.AnalyzeVolume(input));
    }

    [Fact]
    public void PcManagerInstallUsesOnlyOfficialMicrosoftStoreIdentity()
    {
        var command = PcManagerCommandFactory.InstallWithWinGet(
            @"C:\Users\Lab\AppData\Local\Microsoft\WindowsApps\winget.exe");

        Assert.Equal("winget.exe", Path.GetFileName(command.ExecutablePath), ignoreCase: true);
        Assert.Equal(
            [
                "install",
                "--id",
                "9PM860492SZD",
                "--source",
                "msstore",
                "--exact",
                "--interactive",
                "--accept-source-agreements",
                "--accept-package-agreements",
            ],
            command.Arguments);
        Assert.DoesNotContain(command.Arguments, argument =>
            argument.StartsWith("http", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PcManagerFallbackUsesOfficialStoreProductUri()
    {
        var uri = PcManagerCommandFactory.StoreProductPage();

        Assert.Equal("ms-windows-store", uri.Scheme);
        Assert.Contains("9PM860492SZD", uri.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("winget.exe")]
    [InlineData(@"C:\Windows\System32\cmd.exe")]
    public void PcManagerInstallRejectsUnknownExecutable(string path)
    {
        Assert.Throws<ArgumentException>(() => PcManagerCommandFactory.InstallWithWinGet(path));
    }
}
