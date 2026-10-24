param(
    [string]$CandidateDirectory = $PSScriptRoot,
    [string]$OutputRoot = $CandidateDirectory
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Invoke-CandidateProcess.ps1')
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host 'Run VALIDAR-CANDIDATA.cmd as administrator.'
    exit 3
}
$executable = Join-Path $CandidateDirectory 'WinMaint.Cli.exe'
if (-not (Test-Path -LiteralPath $executable)) { throw 'The candidate executable is missing.' }
$verificationDirectory = Join-Path $OutputRoot ('verification-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $verificationDirectory -ErrorAction Stop | Out-Null
$cases = @(
    @{ Name='identity-help'; Arguments=@('--help'); Expected=0 },
    @{ Name='smoke'; Arguments=@('--smoke-test','--no-ansi'); Expected=0 },
    @{ Name='unknown-option'; Arguments=@('--unknown'); Expected=2 },
    @{ Name='missing-path'; Arguments=@('--report-dir'); Expected=2 },
    @{ Name='conflicting-modes'; Arguments=@('--report','--quick-test'); Expected=2 },
    @{ Name='lab-removed'; Arguments=@('--phase4-lab-temp'); Expected=3 },
    @{ Name='pc2-lab-retired'; Arguments=@('--phase5-pc2-lab'); Expected=2 },
    @{ Name='cleanup-flag-unavailable'; Arguments=@('--cleanup'); Expected=2 },
    @{ Name='report'; Arguments=@('--report','--no-ansi','--report-dir',(Join-Path $verificationDirectory 'reports')); Expected=0 },
    @{ Name='inventory-menu'; Arguments=@('--no-ansi','--report-dir',(Join-Path $verificationDirectory 'inventory')); Expected=0; Input="5`n5`n10" }
)
$results = @()
foreach ($case in $cases) {
    Write-Host ('RUNNING: ' + $case.Name)
    $arguments = $case.Arguments
    $outputPath = Join-Path $verificationDirectory ($case.Name + '.txt')
    $menuInput = if ($case.ContainsKey('Input')) { $case.Input } else { '' }
    try {
        $execution = Invoke-CandidateProcess -Executable $executable -ProcessArguments $arguments -MenuInput $menuInput
    } catch {
        $execution = [pscustomobject]@{ ExitCode=-1; Output=('Verifier failure: ' + $_.Exception.Message); InputHex='' }
    }
    $execution.Output | Out-File -LiteralPath $outputPath -Encoding utf8
    $code = $execution.ExitCode
    $output = $execution.Output
    $noAnsi = -not $output.Contains([char]27)
    $validInput = -not $case.ContainsKey('Input') -or
        ($execution.InputPreambleLength -eq 0 -and $execution.SentChoices -eq 3 -and $execution.InputHex -eq '35-0A-35-0A-31-30' -and -not $output.Contains('Entrada no v'))
    $results += [pscustomobject]@{ Name=$case.Name; ExitCode=$code; Expected=$case.Expected; Passed=($code -eq $case.Expected -and $noAnsi -and $validInput); NoAnsi=$noAnsi; InputHex=$execution.InputHex; InputTrace=$execution.InputTrace; InputPreambleLength=$execution.InputPreambleLength }
    Write-Host ('FINISHED: ' + $case.Name)
}
$reportDirectory = Join-Path $verificationDirectory 'reports'
$jsonFiles = @(Get-ChildItem -LiteralPath $reportDirectory -Filter '*.json' -ErrorAction SilentlyContinue)
$report = $null
if ($jsonFiles.Count -eq 1) {
    $report = Get-Content -Raw -LiteralPath $jsonFiles[0].FullName | ConvertFrom-Json
    $textPath = [IO.Path]::ChangeExtension($jsonFiles[0].FullName, '.txt')
    $reportPassed = $report.schemaVersion -eq '1.0' -and $report.status -eq 'Completed' -and $report.snapshots.Count -eq 1 -and (Test-Path -LiteralPath $textPath)
} else { $reportPassed = $false }
$results += [pscustomobject]@{ Name='report-files'; Passed=$reportPassed }
$inventoryDirectory = Join-Path $verificationDirectory 'inventory'
$inventoryFiles = @(Get-ChildItem -LiteralPath $inventoryDirectory -Filter '*.json' -ErrorAction SilentlyContinue)
$inventoryPassed = $false
$inventoryReport = $null
if ($inventoryFiles.Count -eq 1) {
    $inventoryReport = Get-Content -Raw -LiteralPath $inventoryFiles[0].FullName | ConvertFrom-Json
    $records = @($inventoryReport.temporaryCleanup)
    $inventoryPassed = $inventoryReport.schemaVersion -eq '1.1' -and $inventoryReport.status -eq 'Completed' -and
        $inventoryReport.snapshots.Count -eq 1 -and $records.Count -eq 2 -and
        $inventoryReport.actions.Count -eq 0 -and $inventoryReport.stabilityTests.Count -eq 0 -and
        $records[0].inventory.id -ne $records[1].inventory.id -and
        @($records | Where-Object { $_.status -ne 'InventoryOnly' -or $_.files.Count -ne 0 -or
            $_.deletedFiles -ne 0 -or $_.deletedLogicalBytes -ne 0 }).Count -eq 0 -and
        (Test-Path -LiteralPath ([IO.Path]::ChangeExtension($inventoryFiles[0].FullName, '.txt')))
}
$results += [pscustomobject]@{ Name='inventory-structured-read-only'; Passed=$inventoryPassed }
$helpText = Get-Content -Raw -LiteralPath (Join-Path $verificationDirectory 'identity-help.txt')
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($executable).ProductVersion
$identityPassed = $report -and $inventoryReport -and $version.StartsWith('1.0.0-rc.4') -and $helpText.Contains('WinMaint v1.0.0-rc.4') -and
    $report.toolVersion.StartsWith('WinMaint v1.0.0-rc.4') -and
    $inventoryReport.toolVersion.StartsWith('WinMaint v1.0.0-rc.4')
$results += [pscustomobject]@{ Name='rc4-identity'; Passed=$identityPassed }
$summary = [pscustomobject]@{
    SchemaVersion='candidate-verification-1.1'
    VerifierVersion='rc4-no-preamble-5'
    CandidateVersion=$version
    CandidateSHA256=(Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash
    WindowsBuild=[int](Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion').CurrentBuildNumber
    OSArchitecture=$env:PROCESSOR_ARCHITECTURE
    Elevated=$true
    Cases=$results
    Passed=(@($results | Where-Object { -not $_.Passed }).Count -eq 0)
}
$summary | ConvertTo-Json -Depth 5 | Out-File -LiteralPath (Join-Path $verificationDirectory 'RESULTADO.json') -Encoding utf8
$results | Format-Table -AutoSize
Write-Host ('RESULT: ' + $summary.Passed)
Write-Host ('OUTPUT: ' + $verificationDirectory)
exit $(if ($summary.Passed) { 0 } else { 4 })
