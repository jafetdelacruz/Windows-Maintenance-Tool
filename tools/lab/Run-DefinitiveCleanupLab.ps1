$ErrorActionPreference = 'Stop'
if ([Environment]::UserName -ne 'WDAGUtilityAccount' -or
    [System.IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\') -ne 'C:\WinMaintPhase8Cleanup') {
    throw 'Owned Sandbox only. Nothing was launched.'
}
$runId = [Guid]::NewGuid().ToString('N')
$statePath = Join-Path 'C:\WinMaintLabOutput' ("boot-$runId.json")
$logPath = Join-Path 'C:\WinMaintLabOutput' ("boot-$runId.log")
$state = [ordered]@{ SchemaVersion = 'cleanup-boot-1.0'; RunId = $runId; Status = 'Starting'; ExitCode = $null; Error = $null }
$child = $null
try {
    $start = New-Object System.Diagnostics.ProcessStartInfo
    $start.FileName = Join-Path $PSScriptRoot 'WinMaint.CleanupLab.exe'
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.StandardOutputEncoding = New-Object System.Text.UTF8Encoding($false)
    $start.StandardErrorEncoding = New-Object System.Text.UTF8Encoding($false)
    $child = [System.Diagnostics.Process]::Start($start)
    $stdout = $child.StandardOutput.ReadToEndAsync()
    $stderr = $child.StandardError.ReadToEndAsync()
    $state.Status = 'Running'
    [System.IO.File]::WriteAllText($statePath, ($state | ConvertTo-Json), (New-Object System.Text.UTF8Encoding($false)))
    if (-not $child.WaitForExit(240000)) { throw 'Owned lab exceeded its four-minute controller deadline.' }
    [System.IO.File]::WriteAllText($logPath, ($stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()), (New-Object System.Text.UTF8Encoding($false)))
    $state.ExitCode = $child.ExitCode
    $state.Status = 'Completed'
} catch {
    $state.Status = 'Failed'
    $state.Error = $_.Exception.Message
} finally {
    if ($null -ne $child) {
        if (-not $child.HasExited) { $child.Kill(); $child.WaitForExit() }
        $child.Dispose()
    }
    [System.IO.File]::WriteAllText($statePath, ($state | ConvertTo-Json), (New-Object System.Text.UTF8Encoding($false)))
}
