using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WinMaint.Infrastructure.Win;

// Shared handle API. Production composition does not register mutable execution.
internal static class WindowsTemporaryNative
{
    internal const uint DirectoryFlag = 0x02000000;
    private const uint OpenReparsePoint = 0x00200000;
    private const uint ReadAttributes = 0x80;
    private const uint DeleteAccess = 0x10000;
    private const uint ListDirectory = 0x1;
    private const uint Forbidden = (uint)(FileAttributes.ReparsePoint | FileAttributes.Directory |
        FileAttributes.System | FileAttributes.Offline | FileAttributes.ReadOnly);

    internal static SafeFileHandle Open(string path, bool delete, bool directory = false)
    {
        // Attribute-only access does not establish the intended directory share lock.
        var handle = CreateFileW(path, ReadAttributes | (delete ? DeleteAccess : 0) | (directory ? ListDirectory : 0),
            directory ? 3u : delete ? 0u : 7u, IntPtr.Zero, 3,
            OpenReparsePoint | (directory ? DirectoryFlag : 0), IntPtr.Zero);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error);
        }
        return handle;
    }

    internal static FileIdentity Inspect(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var info))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return info;
    }

    internal static string FinalPath(SafeFileHandle handle)
    {
        var buffer = new char[32768];
        uint length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, 0);
        if (length == 0 || length >= buffer.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        string result = new(buffer, 0, (int)length);
        if (!result.StartsWith(@"\\?\C:\", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only local C: lab paths are allowed.");
        return result[4..];
    }

    internal static bool Eligible(FileIdentity info, DateTime cutoff) =>
        (info.Attributes & Forbidden) == 0 && info.Links == 1 &&
        info.Creation > 0 && info.Write > 0 &&
        DateTime.FromFileTimeUtc(info.Creation) <= cutoff &&
        DateTime.FromFileTimeUtc(info.Write) <= cutoff;

    internal static void MarkDeleted(SafeFileHandle handle)
    {
        byte disposition = 1;
        if (!SetFileInformationByHandle(handle, 4, ref disposition, 1))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    internal static void AddHardLink(string link, string target)
    {
        if (!CreateHardLinkW(link, target, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FileIdentity : IEquatable<FileIdentity>
    {
        internal uint Attributes;
        private uint creationLow, creationHigh, accessLow, accessHigh, writeLow, writeHigh;
        internal uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
        internal readonly long Creation => (long)(((ulong)creationHigh << 32) | creationLow);
        internal readonly long Write => (long)(((ulong)writeHigh << 32) | writeLow);
        internal readonly ulong Size => ((ulong)SizeHigh << 32) | SizeLow;
        public readonly bool Equals(FileIdentity other) => Attributes == other.Attributes &&
            Volume == other.Volume && IndexHigh == other.IndexHigh && IndexLow == other.IndexLow &&
            Creation == other.Creation && Write == other.Write && Size == other.Size && Links == other.Links;
        public override readonly bool Equals(object? obj) => obj is FileIdentity other && Equals(other);
        public override readonly int GetHashCode() => HashCode.Combine(Volume, IndexHigh, IndexLow, Creation, Write, Size);
        public static bool operator ==(FileIdentity left, FileIdentity right) => left.Equals(right);
        public static bool operator !=(FileIdentity left, FileIdentity right) => !left.Equals(right);
    }

    // Explicit Win32 ABI: FILE_DISPOSITION_INFO is one BOOLEAN (one byte).
#pragma warning disable SYSLIB1054 // Lab-only, explicitly marshalled Win32 declarations.
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string link, string existing, IntPtr security);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share,
        IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileIdentity info);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, [Out] char[] path, uint size, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int kind, ref byte info, uint size);
#pragma warning restore SYSLIB1054
}
