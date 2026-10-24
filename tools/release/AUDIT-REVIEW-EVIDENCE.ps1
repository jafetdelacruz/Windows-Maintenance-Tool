param(
    [Parameter(Mandatory=$true)][string] $Package,
    [Parameter(Mandatory=$true)][string] $Results,
    [Parameter(Mandatory=$true)][ValidatePattern('^[A-Fa-f0-9]{64}$')][string] $ExpectedCliSha256,
    [Parameter(Mandatory=$true)][ValidatePattern('^[A-Fa-f0-9]{64}$')][string] $ExpectedVerifierSha256,
    [Parameter(Mandatory=$true)][ValidatePattern('^[A-Fa-f0-9]{64}$')][string] $ExpectedManifestSha256
)
$ErrorActionPreference = 'Stop'
$Package = [IO.Path]::GetFullPath($Package).TrimEnd('\')
$Results = [IO.Path]::GetFullPath($Results).TrimEnd('\')
function Resolve-Child($Root, $Relative) {
    if ([IO.Path]::IsPathRooted($Relative) -or $Relative.Contains(':')) { throw 'Relative evidence path required.' }
    $path = [IO.Path]::GetFullPath((Join-Path $Root $Relative))
    if (-not $path.StartsWith($Root + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Path escaped evidence root.' }
    $current = $path
    while ($current.Length -ge $Root.Length) {
        if ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Reparse evidence is not eligible.' }
        $current = Split-Path -Parent $current
    }
    return $path
}
$manifestPath = Resolve-Child $Package 'package-manifest.json'
if ((Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash -ne $ExpectedManifestSha256) { throw 'Pinned manifest mismatch.' }
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$requiredArtifacts = @('WinMaint.Cli.exe','verifier/WinMaint.EvidenceVerifier.exe','VERIFY-REVIEW-CANDIDATE.ps1',
    'Invoke-CandidateProcess.ps1','Invoke-HashPinnedCandidateProcess.ps1','profile.wsb')
if ($manifest.SchemaVersion -ne 'review-r0-package-1.0' -or $manifest.ProductionEnabled -ne $false -or
    $manifest.RuleSetVersion -ne '1.0.4' -or @($manifest.Artifacts).Count -ne 6 -or
    @(Compare-Object $requiredArtifacts @($manifest.Artifacts.Name)).Count -ne 0) { throw 'Invalid pinned package contract.' }
foreach ($artifact in $manifest.Artifacts) {
    $path = Resolve-Child $Package $artifact.Name
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $artifact.Sha256) { throw 'Package artifact mismatch.' }
}
foreach ($source in $manifest.Sources) {
    $path = Resolve-Child (Join-Path $Package 'source-snapshot') $source.Path
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $source.Sha256) { throw 'Frozen source mismatch.' }
}
$cli = Resolve-Child $Package 'WinMaint.Cli.exe'
$verifier = Resolve-Child $Package 'verifier/WinMaint.EvidenceVerifier.exe'
if ((Get-FileHash -LiteralPath $cli -Algorithm SHA256).Hash -ne $ExpectedCliSha256 -or
    (Get-FileHash -LiteralPath $verifier -Algorithm SHA256).Hash -ne $ExpectedVerifierSha256) { throw 'Executable is not the explicitly selected binary.' }
$summary = Get-Content -LiteralPath (Resolve-Child $Results 'RESULTADO.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$processCases = @('identity-help','smoke','unknown-option','missing-path','conflicting-modes','lab-removed',
    'pc2-lab-retired','cleanup-flag-unavailable','report','diagnostic','report-export-failure',
    'diagnostic-export-failure','empty-inventory','nonempty-inventory')
$positiveCases = @('report','diagnostic','empty-inventory','nonempty-inventory')
$negativeCases = @('empty-diagnostic','missing-metric','wrong-version','wrong-rules','duplicate-id','wrong-counter',
    'missing-counter','duplicate-property','forged-score','text-mismatch','empty-vs-nonempty')
$names = @('hash-mismatch-prevents-launch','r0-fixture-survives','exact-product-identity') +
    @($processCases | ForEach-Object { 'process-' + $_ }) + @($positiveCases | ForEach-Object { 'functional-' + $_ }) +
    @($negativeCases | ForEach-Object { 'reject-' + $_ })
if ($summary.Status -ne 'Completed' -or $summary.Passed -ne $true -or $summary.ProductionEnabled -ne $false -or
    $summary.CandidateSHA256 -ne $ExpectedCliSha256 -or $summary.VerifierSHA256 -ne $ExpectedVerifierSha256 -or
    $summary.PackageManifestSHA256 -ne $ExpectedManifestSha256 -or $summary.OSArchitecture -ne 'AMD64' -or
    $summary.Elevated -ne $true -or $summary.WindowsBuild -lt 22000 -or
    @($summary.Cases).Count -ne 32 -or @($summary.Cases.Name | Sort-Object -Unique).Count -ne 32 -or
    @(Compare-Object $names @($summary.Cases.Name)).Count -ne 0 -or
    @($summary.Cases | Where-Object Passed -ne $true).Count -ne 0) { throw 'Incomplete, mismatched or failed VM acceptance.' }
if (@($summary.Artifacts.Path | Sort-Object -Unique).Count -ne @($summary.Artifacts).Count) { throw 'Duplicate artifact identities.' }
$actualPaths = @(Get-ChildItem -LiteralPath $Results -Recurse -File | Where-Object Name -ne 'RESULTADO.json' | ForEach-Object { $_.FullName.Substring($Results.Length + 1) })
if (@(Compare-Object $actualPaths @($summary.Artifacts.Path)).Count -ne 0) { throw 'Extra or missing VM evidence.' }
foreach ($artifact in $summary.Artifacts) {
    $path = Resolve-Child $Results $artifact.Path
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $artifact.Sha256) { throw 'VM evidence checksum mismatch.' }
}
. (Join-Path $Package 'Invoke-HashPinnedCandidateProcess.ps1')
foreach ($name in $processCases) {
    $execution = Get-Content -LiteralPath (Resolve-Child $Results ($name + '.process.json')) -Raw -Encoding UTF8 | ConvertFrom-Json
    $expected = if ($name -in @('unknown-option','missing-path','conflicting-modes','pc2-lab-retired','cleanup-flag-unavailable')) { 2 }
        elseif ($name -eq 'lab-removed') { 3 } elseif ($name.EndsWith('export-failure')) { 4 } else { 0 }
    if ($execution.ExitCode -ne $expected -or $execution.Output.Contains([char]27)) { throw 'Actual process result differs from contract.' }
    if ($name -in @('empty-inventory','nonempty-inventory') -and
        ($execution.SentChoices -ne 3 -or $execution.InputPreambleLength -ne 0 -or $execution.InputHex -ne '35-0A-35-0A-31-30')) { throw 'Menu transport was not exercised.' }
}
$pairs = @{}
foreach ($name in $positiveCases) {
    $directory = if ($name -eq 'report') { 'report with spaces' } else { $name }
    $files = @(Get-ChildItem -LiteralPath (Join-Path $Results $directory) -Filter '*.json')
    if ($files.Count -ne 1) { throw 'One product report per scenario required.' }
    $json = Resolve-Child $Results ($directory + '\' + $files[0].Name)
    $txt = [IO.Path]::ChangeExtension($json, '.txt')
    $requirementsPath = Resolve-Child $Results ($name + '.requirements.json')
    $requirements = Get-Content -LiteralPath $requirementsPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $metricKeys = @('system/system.compatibility','performance/performance.cpu.percent','performance/memory.available.bytes',
        'performance/memory.load.percent','events/events.whea.error.count','storage/storage.volume.free.percent')
    $actualMetrics = @($requirements.requiredMetrics | ForEach-Object { $_.providerId + '/' + $_.metricKey })
    if ($requirements.exactToolVersion -ne $manifest.ToolVersion -or $requirements.exactRuleSetVersion -ne '1.0.4' -or
        $requirements.snapshotCount -ne 1 -or $requirements.inventoryCount -ne $(if ($name.EndsWith('inventory')) { 2 } else { 0 }) -or
        $requirements.requireNonEmptyInventory -ne ($name -eq 'nonempty-inventory') -or
        $requirements.requireEmptyInventory -ne ($name -eq 'empty-inventory') -or
        $actualMetrics.Count -ne 6 -or @(Compare-Object $metricKeys $actualMetrics).Count -ne 0 -or
        @($requirements.requiredMetrics | Where-Object metricKey -eq 'storage.volume.free.percent')[0].datumName -ne 'Volumen C:\' -or
        @($requirements.requiredMetrics | Where-Object { $_.metricKey -ne 'storage.volume.free.percent' -and $_.datumName }).Count -ne 0) { throw 'Functional requirements were weakened.' }
    $result = Invoke-HashPinnedCandidateProcess -Executable $verifier -ExpectedSha256 $ExpectedVerifierSha256 -ProcessArguments @('--r0-report',$json,$requirementsPath,$txt)
    $verdict = $result.Output | ConvertFrom-Json
    if ($result.ExitCode -ne 0 -or $verdict.Accepted -ne $true) { throw ('Portable host revalidation failed: ' + $name + ' ' + $result.Output) }
    $pairs[$name] = @{ Json=$json; Text=$txt; Requirements=$requirementsPath }
}
foreach ($name in $negativeCases) {
    $pair = $pairs['nonempty-inventory']
    $negativeText = if ($name -eq 'text-mismatch') { Resolve-Child $Results 'negative-text.txt' } else { $pair.Text }
    $requirementsPath = if ($name -eq 'empty-vs-nonempty') { $pairs['empty-inventory'].Requirements } else { $pair.Requirements }
    $result = Invoke-HashPinnedCandidateProcess -Executable $verifier -ExpectedSha256 $ExpectedVerifierSha256 -ProcessArguments @('--r0-report',
        (Resolve-Child $Results ('negative-' + $name + '.json')),$requirementsPath,$negativeText)
    $verdict = $result.Output | ConvertFrom-Json
    if ($result.ExitCode -ne 5 -or $verdict.Accepted -ne $false) { throw ('Negative report unexpectedly accepted: ' + $name) }
}
[pscustomobject]@{ Accepted=$true; Cases=32; FunctionalReports=4; RejectedReports=11;
    CandidateSHA256=$ExpectedCliSha256; VerifierSHA256=$ExpectedVerifierSha256; ManifestSHA256=$ExpectedManifestSha256;
    ResultSHA256=(Get-FileHash -LiteralPath (Join-Path $Results 'RESULTADO.json') -Algorithm SHA256).Hash;
    Scope='Windows 11 packaged R0 only. No deletion, workload, visual, Windows 10 or production acceptance.' }
