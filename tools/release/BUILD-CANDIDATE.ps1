param(
    [Parameter(Mandatory=$true)][string]$DotNetPath,
    [string]$OutputDirectory = 'artifacts\release\WinMaint-v1.0.0-rc.4'
)
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
Push-Location $repositoryRoot
try {
    & $DotNetPath restore 'WinMaint.sln' --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Locked restore failed.' }
    & $DotNetPath test 'WinMaint.sln' -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    & $DotNetPath format 'WinMaint.sln' --no-restore --verify-no-changes
    if ($LASTEXITCODE -ne 0) { throw 'Format verification failed.' }
    # Rebuild avoids reusing an apphost with the development manifest.
    & $DotNetPath build 'src\WinMaint.Cli\WinMaint.Cli.csproj' -c Release --no-restore -t:Rebuild -p:PhaseSevenCandidate=true
    if ($LASTEXITCODE -ne 0) { throw 'Candidate rebuild failed.' }
    if (Test-Path -LiteralPath $OutputDirectory) { throw 'Output directory exists; refusing to overwrite a candidate.' }
    & $DotNetPath publish 'src\WinMaint.Cli\WinMaint.Cli.csproj' -c Release --no-restore -p:PhaseSevenCandidate=true -o $OutputDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Candidate publication failed.' }
    $manifest = & (Join-Path $PSScriptRoot 'INSPECT-CANDIDATE.ps1') -ExecutablePath (Join-Path $OutputDirectory 'WinMaint.Cli.exe')
    if ($manifest.RequestedExecutionLevel -ne 'requireAdministrator') { throw 'Unexpected UAC manifest.' }
    $manifest
} finally { Pop-Location }
