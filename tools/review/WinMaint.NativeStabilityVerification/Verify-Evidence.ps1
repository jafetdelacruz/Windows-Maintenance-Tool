param([Parameter(Mandatory)][string]$Batch)
$ErrorActionPreference = 'Stop'
$checks = [Collections.Generic.List[object]]::new()
function Check([string]$Name, [bool]$Passed) {
    $checks.Add([pscustomobject]@{ Name = $Name; Passed = $Passed })
}
function Read-Product([string]$Directory) {
    $files = @(Get-ChildItem -LiteralPath $Directory -Filter 'WinMaint-*.json')
    if ($files.Count -ne 1) { throw "Expected one product JSON in $Directory" }
    $report = Get-Content -LiteralPath $files[0].FullName -Raw | ConvertFrom-Json
    $text = Get-Content -LiteralPath ([IO.Path]::ChangeExtension($files[0].FullName, '.txt')) -Raw
    Check "$Directory JSON/TXT export matches" ($text.Contains('Exportación: ' + ([guid]$report.exportId).ToString('N') + '.'))
    Check "$Directory schema and version" ($report.schemaVersion -eq '1.2' -and $report.toolVersion.Contains('rc.5-dev'))
    Check "$Directory no maintenance actions" ($report.actions.Count -eq 0 -and $report.temporaryCleanup.Count -eq 0 -and $report.decisions.Count -eq 0)
    return $report
}
$full = Read-Product (Join-Path $Batch 'full')
$process = Get-Content -LiteralPath (Join-Path $Batch 'full-process.json') -Raw | ConvertFrom-Json
Check 'CLI success and wall time 240-360 seconds' ($process.exitCode -eq 0 -and $process.wallSeconds -ge 240 -and $process.wallSeconds -le 360)
$test = $full.stabilityTests[0]
Check 'One complete test, ten passed probes' ($full.stabilityTests.Count -eq 1 -and !$test.wasInterrupted -and $test.probes.Count -eq 10 -and @($test.probes | Where-Object status -ne Passed).Count -eq 0)
Check 'Initial and final diagnostic preserved' ($full.snapshots.Count -eq 2)
Check 'No stability findings' ($test.findings.Count -eq 0)
Check 'Comparable WHEA marks unchanged' (($test.probes[0].metrics.'whea.latest.before' -eq 0) -and ($test.probes[-1].metrics.'whea.latest.after' -eq 0))
$lastRefresh = ([DateTimeOffset]$test.startedAt).ToUnixTimeMilliseconds()
foreach ($id in @('cpu', 'memory', 'storage-read')) {
    $probe = $test.probes | Where-Object probeId -eq $id
    $m = $probe.metrics
    Check "$id fresh revalidation" ($m.'safety.refresh.unix-ms' -gt $lastRefresh)
    $lastRefresh = $m.'safety.refresh.unix-ms'
    Check "$id native runtime monitoring" ($m.'safety.runtime.samples' -ge 20 -and $m.'safety.memory.available' -eq 1 -and $m.'safety.power.known' -eq 1)
    Check "$id thermal absence explicit" ($m.'safety.thermal.available' -eq 0 -and $probe.detail.Contains('no disponible'))
}
$ram = ($test.probes | Where-Object probeId -eq memory).metrics
Check 'RAM positive allocation within fresh budget and 1 GiB cap' ($ram.'memory.bytes' -gt 0 -and $ram.'memory.bytes' -le $ram.'safety.memory.budget.bytes' -and $ram.'memory.bytes' -le 1073741824 -and $ram.'memory.bytes' -le ($ram.'safety.memory.before.bytes' * 0.2) -and $ram.'memory.passes' -gt 0)
Check 'CPU observed iterations and sequential read bytes' (($test.probes | Where-Object probeId -eq cpu).metrics.'cpu.iterations' -gt 0 -and ($test.probes | Where-Object probeId -eq storage-read).metrics.'storage.bytes-read' -gt 0)
$cancel = Read-Product (Join-Path $Batch 'cancellation')
$verification = Get-Content -LiteralPath (Join-Path $Batch 'cancellation/verification.json') -Raw | ConvertFrom-Json
Check 'Native cancellation instrumented assertions passed' ($verification.passed -eq $true -and (Get-Content -LiteralPath (Join-Path $Batch 'cancellation-exit.txt')) -eq '0')
Check 'Cancellation product report retained' ($cancel.stabilityTests.Count -eq 1 -and $cancel.stabilityTests[0].wasInterrupted -and ($cancel.stabilityTests[0].probes | Where-Object probeId -eq cpu).status -eq 'Cancelled')
Check 'Interrupted marker archived' (Test-Path -LiteralPath (Join-Path $Batch 'cancellation/interrupted-session.json'))
$counters = @(Get-ChildItem -LiteralPath (Join-Path $Batch 'tests') -Filter '*.trx' | ForEach-Object {
    [xml]$trx = Get-Content -LiteralPath $_.FullName -Raw
    $trx.TestRun.ResultSummary.Counters
})
Check 'Four TRX, 289 passed, zero failed or omitted' ($counters.Count -eq 4 -and ($counters | Measure-Object -Property passed -Sum).Sum -eq 289 -and ($counters | Measure-Object -Property failed -Sum).Sum -eq 0 -and ($counters | Measure-Object -Property notExecuted -Sum).Sum -eq 0)
$failed = @($checks | Where-Object Passed -eq $false)
[pscustomobject]@{ Passed = $failed.Count -eq 0; Checks = $checks } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $Batch 'evidence-validation.json')
$checks | Format-Table -AutoSize
if ($failed.Count -gt 0) { throw "$($failed.Count) native evidence checks failed." }
