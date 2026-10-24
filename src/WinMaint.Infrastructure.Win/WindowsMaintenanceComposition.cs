using WinMaint.Application;

namespace WinMaint.Infrastructure.Win;

public static class WindowsMaintenanceComposition
{
    private const string SandboxUserName = "WDAGUtilityAccount";
    private const string LabFolderName = "WinMaintPhase4Lab";
    internal const string PhaseFivePc2LabFolderName = "WinMaintPhase5Pc2Lab";
    internal const string PhaseFivePc2LabMarkerFileName = "PHASE5-PC2-LAB.marker";
    internal const string PhaseFivePc2LabMarkerText = "WINMAINT_PHASE5_PC2_RECOVERABLE_LAB";

    public static bool IsPhaseFourSandboxLab()
    {
        return IsSandboxExecutableUnder(@"C:\WinMaintLab");
    }

    public static bool IsPhaseFiveSandboxLab() =>
        IsSandboxExecutableUnder(@"C:\WinMaintPhase5");

    public static bool IsPhaseFivePc2Lab()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var executablePath = Environment.ProcessPath;
        var markerPath = string.IsNullOrWhiteSpace(executablePath)
            ? null
            : Path.Combine(Path.GetDirectoryName(executablePath)!, PhaseFivePc2LabMarkerFileName);
        string? markerText = null;
        try
        {
            if (markerPath is not null && File.Exists(markerPath))
            {
                markerText = File.ReadAllText(markerPath).Trim();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        return MeetsPhaseFivePc2LabRequirements(
            OperatingSystem.IsWindows(),
            Environment.OSVersion.Version.Build,
            Environment.Is64BitOperatingSystem,
            executablePath,
            localAppData,
            markerText);
    }

    public static IMaintenanceActionRunner CreatePhaseFourTemporaryLabRunner()
    {
        if (!IsPhaseFourSandboxLab())
        {
            throw new InvalidOperationException(
                "El ejecutor mutable de laboratorio solo está disponible dentro del perfil WinMaint de Windows Sandbox.");
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var temporaryRoot = Path.Combine(Path.GetTempPath(), LabFolderName);
        return new TemporaryFileMaintenanceRunner(temporaryRoot, userProfile);
    }

    private static bool IsDescendant(string candidatePath, string rootPath)
    {
        var relative = Path.GetRelativePath(rootPath, candidatePath);
        return relative.Length > 0 &&
            !Path.IsPathFullyQualified(relative) &&
            !relative.Equals("..", StringComparison.Ordinal) &&
            !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    internal static bool MeetsPhaseFivePc2LabRequirements(
        bool isWindows,
        int operatingSystemBuild,
        bool is64BitOperatingSystem,
        string? executablePath,
        string localAppDataRoot,
        string? markerText)
    {
        if (!isWindows || operatingSystemBuild != 19045 || !is64BitOperatingSystem ||
            string.IsNullOrWhiteSpace(executablePath) || string.IsNullOrWhiteSpace(localAppDataRoot) ||
            !string.Equals(markerText, PhaseFivePc2LabMarkerText, StringComparison.Ordinal))
        {
            return false;
        }

        var expectedRoot = Path.Combine(localAppDataRoot, PhaseFivePc2LabFolderName);
        return IsDescendant(Path.GetFullPath(executablePath), Path.GetFullPath(expectedRoot));
    }

    private static bool IsSandboxExecutableUnder(string expectedExecutableRoot)
    {
        var executablePath = Environment.ProcessPath;
        return OperatingSystem.IsWindows() &&
            string.Equals(Environment.UserName, SandboxUserName, StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(executablePath) &&
            IsDescendant(Path.GetFullPath(executablePath), expectedExecutableRoot);
    }
}
