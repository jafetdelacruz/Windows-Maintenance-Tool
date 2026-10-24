$ErrorActionPreference = 'Stop'
if ([Environment]::UserName -ne 'WDAGUtilityAccount' -or
    [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\') -ne 'C:\WinMaintReviewPackage') { throw 'Owned Sandbox only; nothing launched.' }
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Elevated Sandbox required.' }
$runId = [Guid]::NewGuid().ToString('N')
$output = Join-Path 'C:\WinMaintR0Output' $runId
New-Item -ItemType Directory -Path $output | Out-Null
$utf8 = New-Object Text.UTF8Encoding($false)
function Write-Json($Path, $Value) { [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 90), $utf8) }
$state = [ordered]@{ SchemaVersion='review-r0-run-1.0'; RunId=$runId; Status='Running'; Passed=$false; Cases=@(); Error=$null }
$statePath = Join-Path $output 'RESULTADO.json'
Write-Json $statePath $state
try {
    $manifest = Get-Content (Join-Path $PSScriptRoot 'package-manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($manifest.SchemaVersion -ne 'review-r0-package-1.0' -or $manifest.ProductionEnabled -ne $false -or
        $manifest.RuleSetVersion -ne '1.0.4' -or @($manifest.Artifacts).Count -ne 6) { throw 'Invalid package contract.' }
    foreach ($entry in @($manifest.Artifacts)) {
        $path = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot $entry.Name))
        if (-not $path.StartsWith($PSScriptRoot + '\', [StringComparison]::OrdinalIgnoreCase) -or
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.Sha256) { throw 'Package hash mismatch; no candidate launched.' }
    }
    foreach ($source in @($manifest.Sources)) {
        $sourceRoot = Join-Path $PSScriptRoot 'source-snapshot'
        $path = [IO.Path]::GetFullPath((Join-Path $sourceRoot $source.Path))
        if (-not $path.StartsWith($sourceRoot + '\', [StringComparison]::OrdinalIgnoreCase) -or
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $source.Sha256) { throw 'Source snapshot mismatch.' }
    }
    . (Join-Path $PSScriptRoot 'Invoke-HashPinnedCandidateProcess.ps1')
    $cli = Join-Path $PSScriptRoot 'WinMaint.Cli.exe'
    $verifier = Join-Path $PSScriptRoot 'verifier/WinMaint.EvidenceVerifier.exe'
    $cliHash = @($manifest.Artifacts | Where-Object Name -eq 'WinMaint.Cli.exe')[0].Sha256
    $verifierHash = @($manifest.Artifacts | Where-Object Name -eq 'verifier/WinMaint.EvidenceVerifier.exe')[0].Sha256
    $checks = New-Object 'Collections.Generic.List[object]'
    function Add-Check($Name, $Passed, $Details) { $checks.Add([pscustomobject]@{ Name=$Name; Passed=[bool]$Passed; Details=$Details }) }
    $mismatchRejected = $false
    try { $null = Invoke-HashPinnedCandidateProcess -Executable $cli -ExpectedSha256 ('0' * 64) -ProcessArguments @('--help') }
    catch { $mismatchRejected = $_.Exception.Message -eq 'Candidate SHA256 mismatch. Nothing was launched.' }
    Add-Check 'hash-mismatch-prevents-launch' $mismatchRejected 'Wrong expected hash must reject before Process.Start.'
    $invalidDestination = Join-Path $output 'destination-is-file'
    [IO.File]::WriteAllText($invalidDestination, 'Own VM fixture', $utf8)
    $processCases = @(
        @{ Name='identity-help'; Args=@('--help'); Expected=0 },
        @{ Name='smoke'; Args=@('--smoke-test','--no-ansi'); Expected=0 },
        @{ Name='unknown-option'; Args=@('--unknown'); Expected=2 },
        @{ Name='missing-path'; Args=@('--report-dir'); Expected=2 },
        @{ Name='conflicting-modes'; Args=@('--report','--quick-test'); Expected=2 },
        @{ Name='lab-removed'; Args=@('--phase4-lab-temp'); Expected=3 },
        @{ Name='pc2-lab-retired'; Args=@('--phase5-pc2-lab'); Expected=2 },
        @{ Name='cleanup-flag-unavailable'; Args=@('--cleanup'); Expected=2 },
        @{ Name='report'; Args=@('--report','--no-ansi','--report-dir',(Join-Path $output 'report with spaces')); Expected=0; Directory='report with spaces' },
        @{ Name='diagnostic'; Args=@('--diagnostic','--no-ansi','--report-dir',(Join-Path $output 'diagnostic')); Expected=0; Directory='diagnostic' },
        @{ Name='report-export-failure'; Args=@('--report','--no-ansi','--report-dir',$invalidDestination); Expected=4 },
        @{ Name='diagnostic-export-failure'; Args=@('--diagnostic','--no-ansi','--report-dir',$invalidDestination); Expected=4 },
        @{ Name='empty-inventory'; Args=@('--no-ansi','--report-dir',(Join-Path $output 'empty-inventory')); Expected=0; Directory='empty-inventory'; Input="5`n5`n10" },
        @{ Name='nonempty-inventory'; Args=@('--no-ansi','--report-dir',(Join-Path $output 'nonempty-inventory')); Expected=0; Directory='nonempty-inventory'; Input="5`n5`n10" }
    )
    $fixture = $null
    foreach ($case in $processCases) {
        if ($case.Name -eq 'nonempty-inventory') {
            # The public provider observes the real VM user's Temp; never the host's Temp.
            $fixture = Join-Path ([IO.Path]::GetTempPath()) ('WinMaintR0Review-' + $runId + '.txt')
            [IO.File]::WriteAllText($fixture, 'Fictitious old file. R0 must preserve me.', $utf8)
            [IO.File]::SetCreationTimeUtc($fixture, [DateTime]::UtcNow.AddDays(-14))
            [IO.File]::SetLastWriteTimeUtc($fixture, [DateTime]::UtcNow.AddDays(-14))
            $fixtureHash = (Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash
        }
        $inputText = if ($case.ContainsKey('Input')) { $case.Input } else { '' }
        $execution = Invoke-HashPinnedCandidateProcess -Executable $cli -ExpectedSha256 $cliHash -ProcessArguments $case.Args -MenuInput $inputText
        [IO.File]::WriteAllText((Join-Path $output ($case.Name + '.log')), $execution.Output, $utf8)
        Write-Json (Join-Path $output ($case.Name + '.process.json')) $execution
        $passed = $execution.ExitCode -eq $case.Expected -and -not $execution.Output.Contains([char]27)
        if ($case.ContainsKey('Input')) {
            $passed = $passed -and $execution.SentChoices -eq 3 -and $execution.InputPreambleLength -eq 0 -and
                $execution.InputHex -eq '35-0A-35-0A-31-30' -and -not $execution.Output.Contains('Entrada no v')
        }
        Add-Check ('process-' + $case.Name) $passed ([ordered]@{ Expected=$case.Expected; Actual=$execution.ExitCode })
    }
    Add-Check 'r0-fixture-survives' ((Test-Path -LiteralPath $fixture) -and (Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash -eq $fixtureHash) $fixtureHash
    $helpText = [IO.File]::ReadAllText((Join-Path $output 'identity-help.log'))
    $resourceVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($cli).ProductVersion
    Add-Check 'exact-product-identity' ($helpText.Contains($manifest.ToolVersion) -and
        -not [string]::IsNullOrWhiteSpace($manifest.ResourceVersion) -and
        $resourceVersion.Split('+')[0] -ceq $manifest.ResourceVersion -and
        $manifest.ToolVersion.StartsWith('WinMaint v' + $manifest.ResourceVersion + ' ')) $resourceVersion
    $metrics = @(
        @{ providerId='system'; metricKey='system.compatibility' },
        @{ providerId='performance'; metricKey='performance.cpu.percent' },
        @{ providerId='performance'; metricKey='memory.available.bytes' },
        @{ providerId='performance'; metricKey='memory.load.percent' },
        @{ providerId='events'; metricKey='events.whea.error.count' },
        @{ providerId='storage'; metricKey='storage.volume.free.percent'; datumName='Volumen C:\' }
    )
    $pairs = @{}
    foreach ($case in @($processCases | Where-Object { $_.ContainsKey('Directory') })) {
        $directory = Join-Path $output $case.Directory
        $reports = @(Get-ChildItem -LiteralPath $directory -Filter '*.json')
        if ($reports.Count -ne 1) { throw 'Exactly one JSON required per functional scenario.' }
        $json = $reports[0].FullName
        $txt = [IO.Path]::ChangeExtension($json, '.txt')
        $requirements = [ordered]@{ exactToolVersion=$manifest.ToolVersion; snapshotCount=1;
            inventoryCount=$(if ($case.ContainsKey('Input')) { 2 } else { 0 });
            requireNonEmptyInventory=($case.Name -eq 'nonempty-inventory'); requireEmptyInventory=($case.Name -eq 'empty-inventory');
            requiredMetrics=$metrics; exactRuleSetVersion=$manifest.RuleSetVersion }
        $requirementsPath = Join-Path $output ($case.Name + '.requirements.json')
        Write-Json $requirementsPath $requirements
        $execution = Invoke-HashPinnedCandidateProcess -Executable $verifier -ExpectedSha256 $verifierHash -ProcessArguments @('--r0-report',$json,$requirementsPath,$txt)
        [IO.File]::WriteAllText((Join-Path $output ($case.Name + '.validation.json')), $execution.Output, $utf8)
        $verdict = $execution.Output | ConvertFrom-Json
        Add-Check ('functional-' + $case.Name) ($execution.ExitCode -eq 0 -and $verdict.Accepted -eq $true) $verdict
        $pairs[$case.Name] = @{ Json=$json; Text=$txt; Requirements=$requirementsPath }
    }
    # Derived from actual product output, not fake success reports. Every mutation must reject.
    $pair = $pairs['nonempty-inventory']
    $baseJson = [IO.File]::ReadAllText($pair.Json)
    foreach ($name in @('empty-diagnostic','missing-metric','wrong-version','wrong-rules','duplicate-id','wrong-counter','missing-counter','duplicate-property','forged-score','text-mismatch','empty-vs-nonempty')) {
        $node = $baseJson | ConvertFrom-Json
        $negativeJson = Join-Path $output ('negative-' + $name + '.json')
        $negativeText = $pair.Text
        $negativeRequirements = $pair.Requirements
        switch ($name) {
            'empty-diagnostic' { $node.snapshots[0].diagnostic.sections = @() }
            'missing-metric' { $node.snapshots[0].diagnostic.sections = @($node.snapshots[0].diagnostic.sections | Where-Object id -ne 'performance') }
            'wrong-version' { $node.toolVersion = 'wrong-candidate' }
            'wrong-rules' { $node.finalHealth.ruleSetVersion = '0.0.0' }
            'duplicate-id' { $node.temporaryCleanup[1].inventory.id = $node.temporaryCleanup[0].inventory.id }
            'wrong-counter' { $node.temporaryCleanup[0].deletedFiles = 1 }
            'missing-counter' { $node.temporaryCleanup[0].PSObject.Properties.Remove('deletedFiles') }
            'forged-score' { $node.finalHealth.confidencePercent = -1 }
            'text-mismatch' {
                $negativeText = Join-Path $output 'negative-text.txt'
                [IO.File]::WriteAllText($negativeText, ([IO.File]::ReadAllText($pair.Text) + 'altered'), $utf8)
            }
            'empty-vs-nonempty' { $negativeRequirements = $pairs['empty-inventory'].Requirements }
        }
        if ($name -eq 'duplicate-property') { [IO.File]::WriteAllText($negativeJson, $baseJson.Insert(1, '"status":"Completed",'), $utf8) }
        elseif ($name -in @('text-mismatch','empty-vs-nonempty')) { [IO.File]::WriteAllText($negativeJson, $baseJson, $utf8) }
        else { Write-Json $negativeJson $node }
        $execution = Invoke-HashPinnedCandidateProcess -Executable $verifier -ExpectedSha256 $verifierHash -ProcessArguments @('--r0-report',$negativeJson,$negativeRequirements,$negativeText)
        [IO.File]::WriteAllText((Join-Path $output ('negative-' + $name + '.verdict.json')), $execution.Output, $utf8)
        $verdict = $execution.Output | ConvertFrom-Json
        Add-Check ('reject-' + $name) ($execution.ExitCode -eq 5 -and $verdict.Accepted -eq $false) $verdict
    }
    $state.Cases = @($checks.ToArray())
    $state.Passed = $checks.Count -eq 32 -and @($checks | Where-Object { -not $_.Passed }).Count -eq 0
    $state.Status = 'Completed'
    $state.CandidateSHA256 = $cliHash
    $state.VerifierSHA256 = $verifierHash
    $state.PackageManifestSHA256 = (Get-FileHash (Join-Path $PSScriptRoot 'package-manifest.json') -Algorithm SHA256).Hash
    $state.WindowsBuild = [Environment]::OSVersion.Version.Build
    $state.OSArchitecture = $env:PROCESSOR_ARCHITECTURE
    $state.Elevated = $true
    $state.PowerShellVersion = $PSVersionTable.PSVersion.ToString()
    $state.ProductionEnabled = $false
    $state.Artifacts = @(Get-ChildItem -LiteralPath $output -Recurse -File | Where-Object FullName -ne $statePath | ForEach-Object {
        [pscustomobject]@{ Path=$_.FullName.Substring($output.Length + 1); Sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
} catch {
    $state.Status = 'Failed'
    $state.Error = $_.Exception.Message
    if ($null -ne $checks) { $state.Cases = @($checks.ToArray()) }
} finally { Write-Json $statePath $state }
exit $(if ($state.Passed) { 0 } else { 4 })
