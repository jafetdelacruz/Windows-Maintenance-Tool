$ErrorActionPreference = 'Continue'
$ProgressPreference = 'SilentlyContinue'

$repoRoot = Split-Path -Parent $PSScriptRoot
$reportPath = Join-Path $repoRoot 'artifacts\windows-component-scan.txt'
$reportDirectory = Split-Path -Parent $reportPath
New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null

"=== ScanHealth iniciado: $(Get-Date -Format o) ===" |
    Out-File -LiteralPath $reportPath -Encoding utf8 -Width 300
& dism.exe /Online /Cleanup-Image /ScanHealth /NoRestart 2>&1 |
    Out-File -LiteralPath $reportPath -Append -Encoding utf8 -Width 300
$scanExitCode = $LASTEXITCODE
"SCAN_EXIT_CODE=$scanExitCode" |
    Out-File -LiteralPath $reportPath -Append -Encoding utf8 -Width 300
"=== ScanHealth finalizado: $(Get-Date -Format o) ===" |
    Out-File -LiteralPath $reportPath -Append -Encoding utf8 -Width 300

exit $scanExitCode
