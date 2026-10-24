namespace WinMaint.Application;

public static class LaunchPolicy
{
    public static bool SupportsPlatform(bool isWindows, bool isX64, int build) =>
        isWindows && isX64 && (build == 19045 || build >= 22000);
}
