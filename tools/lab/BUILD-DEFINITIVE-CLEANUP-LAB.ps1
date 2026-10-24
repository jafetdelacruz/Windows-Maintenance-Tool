param([Parameter(Mandatory=$true)][ValidatePattern('^[a-z0-9-]+$')][string] $BuildTag)
$ErrorActionPreference = 'Stop'
$repository = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$sdk = [System.IO.Path]::GetFullPath((Join-Path $repository '../.dotnet/dotnet.exe'))
$package = Join-Path $repository "artifacts/lab/$BuildTag"
$inbox = Join-Path $repository "artifacts/verification/$BuildTag-inbox"
if ((Test-Path -LiteralPath $package) -or (Test-Path -LiteralPath $inbox)) { throw 'Never overwrite a lab package or its inbox.' }
$project = Join-Path $PSScriptRoot 'WinMaint.CleanupLab.Next/WinMaint.CleanupLab.Next.csproj'
[xml]$projectXml = Get-Content -LiteralPath $project -Raw
$labVersion = [string]$projectXml.Project.PropertyGroup.Version
$env:DOTNET_CLI_HOME = [System.IO.Path]::GetFullPath((Join-Path $repository '../.dotnet-home'))
$env:DOTNET_PROCESSOR_COUNT = '1'
Push-Location $repository
try {
    $paths = @(& rg --files src tools/lab/WinMaint.CleanupLab.Next)
    $paths += @('Directory.Build.props', 'Directory.Packages.props', 'NuGet.Config', 'global.json',
        'AGENTS.md', 'SPEC.md', 'ARCHITECTURE.md', 'ROADMAP.md',
        'tools/lab/Run-DefinitiveCleanupLab.ps1', 'tools/lab/BUILD-DEFINITIVE-CLEANUP-LAB.ps1')
    $paths = @($paths | Where-Object { $_ -notmatch '(^|[\\/])(bin|obj)[\\/]' -and
        [System.IO.Path]::GetExtension($_) -in @('.cs', '.csproj', '.props', '.targets', '.json', '.manifest', '.md', '.ps1', '.Config') } | Sort-Object -Unique)
    $sources = @($paths | ForEach-Object { [pscustomobject]@{ Path = $_; Sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash } })
    & $sdk restore $project --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Locked restore failed.' }
    & $sdk publish $project --no-restore -c Release -m:1 -p:UseSharedCompilation=false -o $package
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed. Preserve this attempt; use a new tag.' }
    New-Item -ItemType Directory -Path $inbox | Out-Null
    foreach ($source in $sources) {
        if ((Get-FileHash -LiteralPath $source.Path -Algorithm SHA256).Hash -ne $source.Sha256) {
            throw 'Source changed during publish. This attempt is not eligible; use a new tag.'
        }
        $target = Join-Path (Join-Path $package 'source-snapshot') $source.Path
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath $source.Path -Destination $target
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Run-DefinitiveCleanupLab.ps1') -Destination (Join-Path $package 'Run-Lab.ps1')
    $profile = New-Object System.Xml.XmlDocument
    $profile.LoadXml('<Configuration><VGpu>Disable</VGpu><Networking>Disable</Networking><AudioInput>Disable</AudioInput><VideoInput>Disable</VideoInput><PrinterRedirection>Disable</PrinterRedirection><ClipboardRedirection>Disable</ClipboardRedirection><MemoryInMB>4096</MemoryInMB><MappedFolders><MappedFolder><HostFolder/><SandboxFolder>C:\WinMaintPhase8Cleanup</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder><MappedFolder><HostFolder/><SandboxFolder>C:\WinMaintLabOutput</SandboxFolder><ReadOnly>false</ReadOnly></MappedFolder></MappedFolders><LogonCommand><Command/></LogonCommand></Configuration>')
    $profile.Configuration.MappedFolders.MappedFolder[0].HostFolder = $package
    $profile.Configuration.MappedFolders.MappedFolder[1].HostFolder = $inbox
    $profile.Configuration.LogonCommand.Command = 'powershell.exe -NoProfile -Command "Start-Process -WindowStyle Hidden -FilePath powershell.exe -ArgumentList ''-NoLogo -NoProfile -ExecutionPolicy Bypass -File C:\WinMaintPhase8Cleanup\Run-Lab.ps1'' -Verb RunAs"'
    $profile.Save((Join-Path $package 'profile.wsb'))
    $manifest = [ordered]@{
        SchemaVersion = 'cleanup-package-1.0'; BuildTag = $BuildTag; LabVersion = $labVersion;
        Sdk = [string](& $sdk --version); Configuration = 'Release/win-x64/self-contained/single-file';
        SdkExecution = [ordered]@{ ProcessorCount = $env:DOTNET_PROCESSOR_COUNT; TieredCompilation = $env:DOTNET_TieredCompilation; SharedCompilation = $false; MaxBuildNodes = 1 };
        CreatedUtc = [DateTimeOffset]::UtcNow.ToString('O'); ProductionEnabled = $false;
        SourceIdentity = 'Private source snapshot and hashes; public documentation commit is not source identity.';
        Sources = $sources;
        Artifacts = @('WinMaint.CleanupLab.exe','Run-Lab.ps1','profile.wsb') | ForEach-Object {
            [pscustomobject]@{ Name = $_; Sha256 = (Get-FileHash -LiteralPath (Join-Path $package $_) -Algorithm SHA256).Hash }
        }
    }
    [System.IO.File]::WriteAllText((Join-Path $package 'package-manifest.json'), ($manifest | ConvertTo-Json -Depth 10), (New-Object System.Text.UTF8Encoding($false)))
    Write-Output "Package prepared, not accepted: $package"
    Write-Output "Exclusive output inbox: $inbox"
} finally { Pop-Location }
