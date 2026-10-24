using System.Runtime.InteropServices;

namespace WinMaint.Infrastructure.Win;

internal static partial class NativeMethods
{
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetSystemPowerStatus(out SystemPowerStatus systemPowerStatus);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetSystemTimes(
        out FileTime idleTime,
        out FileTime kernelTime,
        out FileTime userTime);
}

[StructLayout(LayoutKind.Sequential)]
internal struct SystemPowerStatus
{
    internal byte AcLineStatus;
    internal byte BatteryFlag;
    internal byte BatteryLifePercent;
    internal byte SystemStatusFlag;
    internal uint BatteryLifeTime;
    internal uint BatteryFullLifeTime;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct MemoryStatusEx
{
    internal uint Length;
    internal uint MemoryLoad;
    internal ulong TotalPhysical;
    internal ulong AvailablePhysical;
    internal ulong TotalPageFile;
    internal ulong AvailablePageFile;
    internal ulong TotalVirtual;
    internal ulong AvailableVirtual;
    internal ulong AvailableExtendedVirtual;

    internal static MemoryStatusEx Create() => new()
    {
        Length = (uint)Marshal.SizeOf<MemoryStatusEx>(),
    };
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct FileTime
{
    private readonly uint lowDateTime;
    private readonly uint highDateTime;

    internal ulong ToUInt64() => ((ulong)highDateTime << 32) | lowDateTime;
}
