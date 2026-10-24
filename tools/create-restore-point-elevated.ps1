$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$reportPath = Join-Path $repoRoot 'artifacts\restore-point-result.txt'
$reportDirectory = Split-Path -Parent $reportPath
New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null

try {
    "=== Inicio: $(Get-Date -Format o) ===" |
        Out-File -LiteralPath $reportPath -Encoding utf8 -Width 300

    Enable-ComputerRestore -Drive 'C:\'
    'Proteccion del sistema habilitada o ya disponible en C:.' |
        Out-File -LiteralPath $reportPath -Append -Encoding utf8 -Width 300

    Checkpoint-Computer `
        -Description 'Antes de reparar Windows Sandbox' `
        -RestorePointType 'MODIFY_SETTINGS'
    'Solicitud de punto de restauracion completada.' |
        Out-File -LiteralPath $reportPath -Append -Encoding utf8 -Width 300

    $restorePoint = Get-ComputerRestorePoint |
        Sort-Object SequenceNumber -Descending |
        Where-Object Description -eq 'Antes de reparar Windows Sandbox' |
        Select-Object -First 1 SequenceNumber, Description, CreationTime, RestorePointType

    if ($null -eq $restorePoint) {
        throw 'Windows no devolvio el punto de restauracion despues de crearlo.'
    }

    $restorePoint |
        Format-List |
        Out-File -LiteralPath $reportPath -Append -Encoding utf8 -Width 300
    "=== Exito: $(Get-Date -Format o) ===" |
        Out-File -LiteralPath $reportPath -Append -Encoding utf8 -Width 300
    exit 0
}
catch {
    "ERROR: $($_.Exception.Message)" |
        Out-File -LiteralPath $reportPath -Append -Encoding utf8 -Width 300
    "=== Fallo: $(Get-Date -Format o) ===" |
        Out-File -LiteralPath $reportPath -Append -Encoding utf8 -Width 300
    exit 1
}
