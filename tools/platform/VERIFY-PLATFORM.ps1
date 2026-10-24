param(
    [Parameter(Mandatory=$true)][ValidatePattern('^[A-Fa-f0-9]{64}$')][string] $ExpectedBundleSha256,
    [string] $OutputRoot = ''
)
$ErrorActionPreference = 'Stop'
# Windows PowerShell -File can bind defaults before PSScriptRoot is available.
# Resolve the default in the script body, never in the parameter declaration.
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $PSScriptRoot 'RESULTS'
}
$bundlePath = Join-Path $PSScriptRoot 'bundle.json'
if ((Get-FileHash -LiteralPath $bundlePath -Algorithm SHA256).Hash -ne $ExpectedBundleSha256) { throw 'Bundle manifest mismatch; nothing launched.' }
$bundle = Get-Content -LiteralPath $bundlePath -Raw -Encoding UTF8 | ConvertFrom-Json
$names = @('WinMaint.Cli.exe','WinMaint.EvidenceVerifier.exe','VERIFY-PLATFORM.ps1','Invoke-CandidateProcess.ps1','Invoke-HashPinnedCandidateProcess.ps1')
if ($bundle.SchemaVersion -ne 'platform-r0-bundle-1.0' -or $bundle.ProductionEnabled -ne $false -or
    @($bundle.Files).Count -ne $names.Count -or @(Compare-Object $names @($bundle.Files.Name)).Count -ne 0) { throw 'Invalid bundle profile.' }
foreach ($file in $bundle.Files) {
    $path = Join-Path $PSScriptRoot $file.Name
    if ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Reparse bundle file rejected.' }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.Sha256) { throw 'Bundle file mismatch; nothing launched.' }
}
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run VALIDATE.cmd as administrator. No tests started.' }
$build = [int](Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion').CurrentBuildNumber
if ($env:PROCESSOR_ARCHITECTURE -ne 'AMD64' -or $build -lt 19045) { throw 'Windows 10 22H2 / Windows 11 x64 required.' }
. (Join-Path $PSScriptRoot 'Invoke-HashPinnedCandidateProcess.ps1')
$cli = Join-Path $PSScriptRoot 'WinMaint.Cli.exe'
$verifier = Join-Path $PSScriptRoot 'WinMaint.EvidenceVerifier.exe'
$cliHash = @($bundle.Files | Where-Object Name -eq 'WinMaint.Cli.exe')[0].Sha256
$verifierHash = @($bundle.Files | Where-Object Name -eq 'WinMaint.EvidenceVerifier.exe')[0].Sha256
$runId = [Guid]::NewGuid().ToString('N')
$output = Join-Path ([IO.Path]::GetFullPath($OutputRoot)) $runId
New-Item -ItemType Directory -Path $output -ErrorAction Stop | Out-Null
$utf8 = New-Object Text.UTF8Encoding($false)
function Write-Json($Path, $Value) { [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 90), $utf8) }
$checks = New-Object 'Collections.Generic.List[object]'
function Add-Check($Name, $Passed) { $checks.Add([pscustomobject]@{ Name=$Name; Passed=[bool]$Passed }) }
$state = [ordered]@{ SchemaVersion='platform-r0-run-1.0'; RunId=$runId; Status='Running'; Passed=$false;
    WindowsBuild=$build; OSArchitecture=$env:PROCESSOR_ARCHITECTURE; Elevated=$true;
    BundleSHA256=$ExpectedBundleSha256; CandidateSHA256=$cliHash; VerifierSHA256=$verifierHash;
    ProductionEnabled=$false; Cases=@(); Artifacts=@(); Error=$null;
    Scope='Packaged R0 only. No workload, deletion, interactive keys, visual or UAC acceptance.' }
Write-Json (Join-Path $output 'RESULTADO.json') $state
try {
    $cases = @(
        @{ Name='identity-help'; Args=@('--help'); Expected=0 },
        @{ Name='smoke'; Args=@('--smoke-test','--no-ansi'); Expected=0 },
        @{ Name='unknown-option'; Args=@('--unknown'); Expected=2 },
        @{ Name='missing-path'; Args=@('--report-dir'); Expected=2 },
        @{ Name='conflicting-modes'; Args=@('--report','--quick-test'); Expected=2 },
        @{ Name='lab-removed'; Args=@('--phase4-lab-temp'); Expected=3 },
        @{ Name='pc2-lab-retired'; Args=@('--phase5-pc2-lab'); Expected=2 },
        @{ Name='cleanup-unavailable'; Args=@('--cleanup'); Expected=2 },
        @{ Name='report'; Args=@('--report','--no-ansi','--report-dir',(Join-Path $output 'report with spaces')); Expected=0; Directory='report with spaces' },
        @{ Name='diagnostic'; Args=@('--diagnostic','--no-ansi','--report-dir',(Join-Path $output 'diagnostic')); Expected=0; Directory='diagnostic' },
        @{ Name='inventory'; Args=@('--no-ansi','--report-dir',(Join-Path $output 'inventory')); Expected=0; Directory='inventory'; Input="5`n5`n10" }
    )
    foreach ($case in $cases) {
        Write-Host ('RUNNING: ' + $case.Name)
        $inputText = if ($case.ContainsKey('Input')) { $case.Input } else { '' }
        $execution = Invoke-HashPinnedCandidateProcess -Executable $cli -ExpectedSha256 $cliHash -ProcessArguments $case.Args -MenuInput $inputText
        Write-Json (Join-Path $output ($case.Name + '.process.json')) $execution
        [IO.File]::WriteAllText((Join-Path $output ($case.Name + '.log')), $execution.Output, $utf8)
        $passed = $execution.ExitCode -eq $case.Expected -and -not $execution.Output.Contains([char]27)
        if ($case.ContainsKey('Input')) {
            $passed = $passed -and $execution.InputPreambleLength -eq 0 -and $execution.SentChoices -eq 3 -and $execution.InputHex -eq '35-0A-35-0A-31-30'
        }
        Add-Check ('process-' + $case.Name) $passed
    }
    $resource = [Diagnostics.FileVersionInfo]::GetVersionInfo($cli).ProductVersion
    $helpText = [IO.File]::ReadAllText((Join-Path $output 'identity-help.log'))
    Add-Check 'exact-identity' ($resource.Split('+')[0] -ceq $bundle.ResourceVersion -and $helpText.Contains($bundle.ToolVersion))
    foreach ($case in @($cases | Where-Object { $_.ContainsKey('Directory') })) {
        $reports = @(Get-ChildItem -LiteralPath (Join-Path $output $case.Directory) -Filter '*.json')
        if ($reports.Count -ne 1) { throw 'Exactly one product JSON per scenario required.' }
        $json = $reports[0].FullName
        $requirements = [ordered]@{ exactToolVersion=$bundle.ToolVersion; exactRuleSetVersion='1.0.4'; snapshotCount=1;
            inventoryCount=$(if ($case.Name -eq 'inventory') { 2 } else { 0 }); requireNonEmptyInventory=$false; requireEmptyInventory=$false;
            requiredMetrics=@(
                @{ providerId='system'; metricKey='system.compatibility' },
                @{ providerId='performance'; metricKey='performance.cpu.percent' },
                @{ providerId='performance'; metricKey='memory.available.bytes' },
                @{ providerId='performance'; metricKey='memory.load.percent' },
                @{ providerId='events'; metricKey='events.whea.error.count' },
                @{ providerId='storage'; metricKey='storage.volume.free.percent'; datumName='Volumen C:\' }) }
        $requirementsPath = Join-Path $output ($case.Name + '.requirements.json')
        Write-Json $requirementsPath $requirements
        $execution = Invoke-HashPinnedCandidateProcess -Executable $verifier -ExpectedSha256 $verifierHash -ProcessArguments @('--r0-report',$json,$requirementsPath,[IO.Path]::ChangeExtension($json,'.txt'))
        Write-Json (Join-Path $output ($case.Name + '.validation.json')) $execution
        $verdict = $execution.Output | ConvertFrom-Json
        Add-Check ('content-' + $case.Name) ($execution.ExitCode -eq 0 -and $verdict.Accepted -eq $true)
    }
    $state.Status = 'Completed'
    $state.Passed = $checks.Count -eq 15 -and @($checks | Where-Object { -not $_.Passed }).Count -eq 0
} catch {
    $state.Status = 'Failed'
    $state.Error = $_.Exception.Message
} finally {
    $state.Cases = @($checks.ToArray())
    $state.Artifacts = @(Get-ChildItem -LiteralPath $output -File -Recurse | Where-Object Name -ne 'RESULTADO.json' | ForEach-Object {
        [pscustomobject]@{ Name=$_.FullName.Substring($output.Length + 1); Sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
    Write-Json (Join-Path $output 'RESULTADO.json') $state
    Write-Host ('RESULT: ' + $state.Passed)
    if ($state.Error) { Write-Host $state.Error }
    Write-Host ('OUTPUT: ' + $output)
    Write-Host 'No files deleted. Production cleanup remains blocked.'
}
exit $(if ($state.Passed) { 0 } else { 4 })
