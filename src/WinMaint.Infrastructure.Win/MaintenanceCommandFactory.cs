namespace WinMaint.Infrastructure.Win;

internal sealed record MaintenanceCommand(string ExecutablePath, IReadOnlyList<string> Arguments);

internal static class MaintenanceCommandFactory
{
    private static readonly string SystemDirectory =
        Environment.GetFolderPath(Environment.SpecialFolder.System);

    public static MaintenanceCommand ChkdskScan(string driveLetter) => new(
        Path.Combine(SystemDirectory, "chkdsk.exe"),
        [$"{NormalizeDriveLetter(driveLetter)}:", "/scan"]);

    public static MaintenanceCommand AnalyzeVolume(string driveLetter) => new(
        Path.Combine(SystemDirectory, "defrag.exe"),
        [$"{NormalizeDriveLetter(driveLetter)}:", "/A", "/U", "/V"]);

    public static MaintenanceCommand OptimizeVolume(string driveLetter) => new(
        Path.Combine(SystemDirectory, "defrag.exe"),
        [$"{NormalizeDriveLetter(driveLetter)}:", "/O", "/U", "/V"]);

    public static MaintenanceCommand DismScanHealth() => new(
        Path.Combine(SystemDirectory, "Dism.exe"),
        ["/Online", "/Cleanup-Image", "/ScanHealth"]);

    public static MaintenanceCommand DismRestoreHealth() => new(
        Path.Combine(SystemDirectory, "Dism.exe"),
        ["/Online", "/Cleanup-Image", "/RestoreHealth"]);

    public static MaintenanceCommand SfcScanNow() => new(
        Path.Combine(SystemDirectory, "sfc.exe"),
        ["/scannow"]);

    private static char NormalizeDriveLetter(string driveLetter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(driveLetter);
        var value = driveLetter.Trim().TrimEnd(':');
        if (value.Length != 1 || !char.IsAsciiLetter(value[0]))
        {
            throw new ArgumentException("Se requiere una sola letra de unidad.", nameof(driveLetter));
        }

        return char.ToUpperInvariant(value[0]);
    }
}

internal static class PcManagerCommandFactory
{
    public const string MicrosoftStoreId = "9PM860492SZD";
    public const string OfficialStoreUri = "ms-windows-store://pdp/?ProductId=9PM860492SZD";

    public static MaintenanceCommand InstallWithWinGet(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        var fullPath = Path.GetFullPath(executablePath);
        if (!Path.IsPathFullyQualified(executablePath) ||
            !string.Equals(Path.GetFileName(fullPath), "winget.exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Se requiere una ruta absoluta a winget.exe.", nameof(executablePath));
        }

        return new MaintenanceCommand(
            fullPath,
            [
                "install",
                "--id",
                MicrosoftStoreId,
                "--source",
                "msstore",
                "--exact",
                "--interactive",
                "--accept-source-agreements",
                "--accept-package-agreements",
            ]);
    }

    public static Uri StoreProductPage() => new(OfficialStoreUri, UriKind.Absolute);
}
