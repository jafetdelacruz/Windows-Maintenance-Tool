param([Parameter(Mandatory=$true)][string]$ExecutablePath)
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class WinMaintManifestReader {
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    public static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);
    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")]
    public static extern IntPtr LockResource(IntPtr resource);
    [DllImport("kernel32.dll")]
    public static extern uint SizeofResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")]
    public static extern bool FreeLibrary(IntPtr module);
}
'@
$module = [WinMaintManifestReader]::LoadLibraryEx([IO.Path]::GetFullPath($ExecutablePath), [IntPtr]::Zero, 2)
if ($module -eq [IntPtr]::Zero) { throw 'Unable to read executable resources.' }
try {
    $resource = [WinMaintManifestReader]::FindResource($module, [IntPtr]1, [IntPtr]24)
    if ($resource -eq [IntPtr]::Zero) { throw 'Missing executable manifest.' }
    $size = [WinMaintManifestReader]::SizeofResource($module, $resource)
    $pointer = [WinMaintManifestReader]::LockResource([WinMaintManifestReader]::LoadResource($module, $resource))
    $bytes = New-Object byte[] $size
    [Runtime.InteropServices.Marshal]::Copy($pointer, $bytes, 0, $size)
    [xml]$manifest = [Text.Encoding]::UTF8.GetString($bytes).Trim([char]0,[char]0xFEFF)
    $manager = New-Object Xml.XmlNamespaceManager($manifest.NameTable)
    $manager.AddNamespace('v3','urn:schemas-microsoft-com:asm.v3')
    $node = $manifest.SelectSingleNode('//v3:requestedExecutionLevel', $manager)
    [pscustomobject]@{ RequestedExecutionLevel=$node.level; UIAccess=$node.uiAccess }
} finally { [WinMaintManifestReader]::FreeLibrary($module) | Out-Null }
