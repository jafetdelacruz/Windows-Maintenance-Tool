$ErrorActionPreference = 'Stop'
if ([Environment]::UserName -ne 'WDAGUtilityAccount' -or $PSScriptRoot -ne 'C:\PlatformTools') {
    throw 'Owned Sandbox regression only.'
}
$target = 'C:\PlatformOutput\Launcher Regression With Spaces'
if (Test-Path -LiteralPath $target) { throw 'Preserve previous regression attempt.' }
New-Item -ItemType Directory -Path $target | Out-Null
foreach ($name in @('WinMaint.Cli.exe','WinMaint.EvidenceVerifier.exe','VERIFY-PLATFORM.ps1',
    'Invoke-CandidateProcess.ps1','Invoke-HashPinnedCandidateProcess.ps1','bundle.json','VALIDATE.cmd','LEEME.txt')) {
    Copy-Item -LiteralPath (Join-Path 'C:\PlatformSource' $name) -Destination (Join-Path $target $name)
}
# Execute the actual shipped CMD, without OutputRoot, from an unrelated cwd.
# NUL only answers its final pause; all CLI menu input is provided by the verifier.
Set-Location 'C:\Windows\System32'
$start = New-Object Diagnostics.ProcessStartInfo
$start.FileName = 'C:\Windows\System32\cmd.exe'
$start.Arguments = '/d /c ""C:\PlatformOutput\Launcher Regression With Spaces\VALIDATE.cmd" < NUL > C:\PlatformOutput\launcher-regression.log 2>&1"'
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$process = [Diagnostics.Process]::Start($start)
$process.WaitForExit()
$code = $process.ExitCode
$process.Dispose()
[pscustomobject]@{ ExitCode=$code; DefaultOutputRootUsed=$true; ShippedCmdExecuted=$true } |
    ConvertTo-Json | Out-File -LiteralPath 'C:\PlatformOutput\launcher-result.json' -Encoding utf8
exit $code
