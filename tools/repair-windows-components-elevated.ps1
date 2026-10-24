$ErrorActionPreference = 'Continue'
$ProgressPreference = 'SilentlyContinue'

$repoRoot = Split-Path -Parent $PSScriptRoot
$reportPath = Join-Path $repoRoot 'artifacts\windows-component-repair.txt'
$reportDirectory = Split-Path -Parent $reportPath
New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null

"=== Reparacion iniciada: $(Get-Date -Format o) ===" |
    Out-File -LiteralPath $reportPath -Encoding utf8 -Width 300

'=== DISM RestoreHealth ===' |
    Out-File -LiteralPath $reportPath -Append -Encoding utf8 -Width 300
& dism.exe /Online /Cleanup-Image /RestoreHealth /NoRestart 2>&1 |
    Out-File -LiteralPath $reportPath -Append -Encoding utf8 -Width 300
$dismExitCode = $LASTEXITCODE
"DISM_EXIT_CODE=$dismExitCode" |
    Out-File -LiteralPath $reportPath -Append -Encoding utf8 -Width 300

if ($dismExitCode -eq 0) {
    '=== SFC ScanNow ===' |
        Out-File -LiteralPath $reportPath -Append -Encoding utf8 -Width 300
    & sfc.exe /scannow 2>&1 |
        Out-File -LiteralPath $reportPath -Append -Encoding utf8 -Width 300
    $sfcExitCode = $LASTEXITCODE
    "SFC_EXIT_CODE=$sfcExitCode" |
        Out-File -LiteralPath $reportPath -Append -Encoding utf8 -Width 300
}
else {
    'SFC omitido porque DISM no pudo completar la reparacion.' |
        Out-File -LiteralPath $reportPath -Append -Encoding utf8 -Width 300
}

"=== Reparacion finalizada: $(Get-Date -Format o) ===" |
    Out-File -LiteralPath $reportPath -Append -Encoding utf8 -Width 300

exit $dismExitCode
