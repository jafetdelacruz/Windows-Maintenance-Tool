param([Parameter(Mandatory=$true)][ValidatePattern('^[a-z0-9-]+$')][string] $BuildTag)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$sdk = [IO.Path]::GetFullPath((Join-Path $repository '../.dotnet/dotnet.exe'))
$package = Join-Path $repository "artifacts/release/$BuildTag"
$inbox = Join-Path $repository "artifacts/verification/$BuildTag-inbox"
$qa = Join-Path $repository "artifacts/verification/$BuildTag-qa"
if ((Test-Path -LiteralPath $package) -or (Test-Path -LiteralPath $inbox) -or (Test-Path -LiteralPath $qa)) { throw 'Never overwrite a package, inbox or QA attempt; use a new build tag.' }
# Tool-process constraint only; no system settings or analyzer changes.
[Diagnostics.Process]::GetCurrentProcess().ProcessorAffinity = [IntPtr]1
$env:DOTNET_CLI_HOME = [IO.Path]::GetFullPath((Join-Path $repository '../.dotnet-home'))
$env:DOTNET_PROCESSOR_COUNT = '1'
$env:DOTNET_TieredCompilation = '0'
$env:DOTNET_ReadyToRun = '1'
Push-Location $repository
try {
    $paths = @(& rg --files src tests tools/review tools/release)
    $paths += @('Directory.Build.props','Directory.Packages.props','NuGet.Config','global.json','WinMaint.sln',
        'AGENTS.md','SPEC.md','ARCHITECTURE.md','ROADMAP.md')
    $paths = @($paths | Where-Object { $_ -notmatch '(^|[\\/])(bin|obj)[\\/]' -and
        [IO.Path]::GetExtension($_) -in @('.cs','.csproj','.props','.targets','.json','.manifest','.md','.ps1','.Config','.sln') } | Sort-Object -Unique)
    $sources = @($paths | ForEach-Object { [pscustomobject]@{ Path=$_; Sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash } })
    & $sdk restore WinMaint.sln --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Solution locked restore failed.' }
    & $sdk restore tools/review/WinMaint.EvidenceVerifier/WinMaint.EvidenceVerifier.csproj --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Verifier locked restore failed.' }
    & $sdk build WinMaint.sln -c Release --no-restore -m:1 -p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) { throw 'Solution build failed.' }
    & $sdk test WinMaint.sln -c Release --no-build --no-restore --logger trx --results-directory $qa
    if ($LASTEXITCODE -ne 0) { throw 'Regression failed.' }
    & $sdk format WinMaint.sln --no-restore --verify-no-changes
    if ($LASTEXITCODE -ne 0) { throw 'Format verification failed.' }
    & $sdk build src/WinMaint.Cli/WinMaint.Cli.csproj -c Release --no-restore -t:Rebuild -m:1 -p:UseSharedCompilation=false -p:PhaseSevenCandidate=true
    if ($LASTEXITCODE -ne 0) { throw 'CLI rebuild failed.' }
    & $sdk publish src/WinMaint.Cli/WinMaint.Cli.csproj -c Release --no-restore -m:1 -p:UseSharedCompilation=false -p:PhaseSevenCandidate=true -o $package
    if ($LASTEXITCODE -ne 0) { throw 'CLI publish failed. Preserve attempt.' }
    & $sdk publish tools/review/WinMaint.EvidenceVerifier/WinMaint.EvidenceVerifier.csproj -c Release --no-restore -m:1 -p:UseSharedCompilation=false -o (Join-Path $package 'verifier')
    if ($LASTEXITCODE -ne 0) { throw 'Verifier publish failed. Preserve attempt.' }
    $uac = & (Join-Path $PSScriptRoot 'INSPECT-CANDIDATE.ps1') -ExecutablePath (Join-Path $package 'WinMaint.Cli.exe')
    if ($uac.RequestedExecutionLevel -ne 'requireAdministrator' -or $uac.UIAccess -ne 'false') { throw 'Invalid CLI UAC manifest.' }
    foreach ($name in @('VERIFY-REVIEW-CANDIDATE.ps1','Invoke-CandidateProcess.ps1','Invoke-HashPinnedCandidateProcess.ps1')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $package $name)
    }
    foreach ($source in $sources) {
        if ((Get-FileHash -LiteralPath $source.Path -Algorithm SHA256).Hash -ne $source.Sha256) { throw 'Sources changed during build; package is not eligible.' }
        $target = Join-Path (Join-Path $package 'source-snapshot') $source.Path
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath $source.Path -Destination $target
    }
    New-Item -ItemType Directory -Path $inbox | Out-Null
    $profile = New-Object Xml.XmlDocument
    $profile.LoadXml('<Configuration><VGpu>Disable</VGpu><Networking>Disable</Networking><AudioInput>Disable</AudioInput><VideoInput>Disable</VideoInput><PrinterRedirection>Disable</PrinterRedirection><ClipboardRedirection>Disable</ClipboardRedirection><MemoryInMB>4096</MemoryInMB><MappedFolders><MappedFolder><HostFolder/><SandboxFolder>C:\WinMaintReviewPackage</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder><MappedFolder><HostFolder/><SandboxFolder>C:\WinMaintR0Output</SandboxFolder><ReadOnly>false</ReadOnly></MappedFolder></MappedFolders><LogonCommand><Command/></LogonCommand></Configuration>')
    $profile.Configuration.MappedFolders.MappedFolder[0].HostFolder = $package
    $profile.Configuration.MappedFolders.MappedFolder[1].HostFolder = $inbox
    $profile.Configuration.LogonCommand.Command = 'powershell.exe -NoProfile -Command "Start-Process -WindowStyle Hidden -FilePath powershell.exe -ArgumentList ''-NoLogo -NoProfile -ExecutionPolicy Bypass -File C:\WinMaintReviewPackage\VERIFY-REVIEW-CANDIDATE.ps1'' -Verb RunAs"'
    $profile.Save((Join-Path $package 'profile.wsb'))
    $identitySource = Get-Content src/WinMaint.Domain/ProductIdentity.cs -Raw -Encoding UTF8
    $toolVersion = [regex]::Match($identitySource, 'DisplayVersion = "([^"]+)"').Groups[1].Value
    if (-not $toolVersion) { throw 'Missing exact product identity.' }
    [xml]$project = Get-Content src/WinMaint.Cli/WinMaint.Cli.csproj -Raw
    $resourceVersion = [string]@($project.Project.PropertyGroup.Version | Where-Object { $_ })[0]
    if (-not $resourceVersion -or -not $toolVersion.StartsWith('WinMaint v' + $resourceVersion + ' ')) { throw 'Inconsistent product and resource identities.' }
    $manifest = [ordered]@{
        SchemaVersion='review-r0-package-1.0'; BuildTag=$BuildTag; ToolVersion=$toolVersion; ResourceVersion=$resourceVersion; RuleSetVersion='1.0.4';
        Sdk=[string](& $sdk --version); CreatedUtc=[DateTimeOffset]::UtcNow.ToString('O');
        Configuration='Release/win-x64/self-contained/single-file'; ProductionEnabled=$false; UacManifest=$uac;
        SourceIdentity='Private frozen sources; public documentation commit is not source identity.';
        BuildProcess=[ordered]@{ Affinity=1; ProcessorCount=1; TieredCompilation=0; ReadyToRun=1; SharedCompilation=$false; MaxBuildNodes=1 };
        Sources=$sources;
        Artifacts=@('WinMaint.Cli.exe','verifier/WinMaint.EvidenceVerifier.exe','VERIFY-REVIEW-CANDIDATE.ps1',
            'Invoke-CandidateProcess.ps1','Invoke-HashPinnedCandidateProcess.ps1','profile.wsb') | ForEach-Object {
            [pscustomobject]@{ Name=$_; Sha256=(Get-FileHash -LiteralPath (Join-Path $package $_) -Algorithm SHA256).Hash }
        }
    }
    [IO.File]::WriteAllText((Join-Path $package 'package-manifest.json'), ($manifest | ConvertTo-Json -Depth 10), (New-Object Text.UTF8Encoding($false)))
    Write-Output "Prepared, NOT accepted: $package"
} finally { Pop-Location }
