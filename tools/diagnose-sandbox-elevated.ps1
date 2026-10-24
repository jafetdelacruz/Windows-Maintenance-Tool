$ErrorActionPreference = 'Continue'
$ProgressPreference = 'SilentlyContinue'

$repoRoot = Split-Path -Parent $PSScriptRoot
$reportPath = Join-Path $repoRoot 'artifacts\sandbox-elevated-diagnostics.txt'
$reportDirectory = Split-Path -Parent $reportPath
New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null

& {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]$identity

    '=== Contexto ==='
    "Timestamp: $(Get-Date -Format o)"
    "Administrador: $($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))"

    '=== Sistema ==='
    Get-CimInstance Win32_OperatingSystem |
        Select-Object Caption, Version, OSArchitecture, LastBootUpTime |
        Format-List
    Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' |
        Select-Object DisplayVersion, CurrentBuild, UBR, BuildLabEx |
        Format-List

    '=== Caracteristicas de virtualizacion ==='
    $featureNames = @(
        'Containers-DisposableClientVM',
        'Microsoft-Hyper-V-All',
        'VirtualMachinePlatform',
        'HypervisorPlatform',
        'Containers'
    )
    foreach ($featureName in $featureNames) {
        Get-WindowsOptionalFeature -Online -FeatureName $featureName -ErrorAction SilentlyContinue |
            Select-Object FeatureName, State, RestartNeeded |
            Format-List
    }

    '=== Hipervisor y servicios ==='
    Get-CimInstance Win32_ComputerSystem | Select-Object HypervisorPresent | Format-List
    Get-Service vmcompute, hns, HvHost, bfe, mpssvc -ErrorAction SilentlyContinue |
        Sort-Object Name |
        Format-Table Name, Status, StartType -AutoSize
    & bcdedit.exe /enum '{current}'

    '=== Grupo Hyper-V ==='
    Get-LocalGroup | Where-Object SID -eq 'S-1-5-32-578' | Format-List Name, SID, Description
    Get-LocalGroupMember -SID 'S-1-5-32-578' -ErrorAction SilentlyContinue |
        Format-Table Name, ObjectClass, PrincipalSource -AutoSize

    '=== Cliente moderno de Windows Sandbox ==='
    Get-AppxPackage -AllUsers -Name 'MicrosoftWindows.WindowsSandbox' |
        Select-Object Name, PackageFullName, Version, Status, InstallLocation, SignatureKind |
        Format-List
    Get-AppxProvisionedPackage -Online |
        Where-Object DisplayName -match 'WindowsSandbox' |
        Select-Object DisplayName, PackageName, Version, InstallLocation |
        Format-List

    '=== Paquetes del componente Sandbox ==='
    Get-WindowsPackage -Online |
        Where-Object PackageName -match 'UtilityVM|DisposableClientVM|Containers' |
        Select-Object PackageName, PackageState, ReleaseType, InstallTime |
        Sort-Object PackageName |
        Format-Table -AutoSize

    '=== Estado del almacen de componentes ==='
    & dism.exe /Online /Cleanup-Image /CheckHealth

    '=== Eventos recientes relevantes ==='
    $startTime = (Get-Date).AddDays(-7)
    $eventLogs = @(
        'Microsoft-Windows-AppModel-Runtime/Admin',
        'Microsoft-Windows-Hyper-V-Compute-Admin',
        'Microsoft-Windows-Hyper-V-Worker-Admin',
        'Microsoft-Windows-Host-Network-Service-Admin',
        'Microsoft-Windows-WindowsUpdateClient/Operational'
    )
    foreach ($eventLog in $eventLogs) {
        "--- $eventLog ---"
        Get-WinEvent -FilterHashtable @{ LogName = $eventLog; StartTime = $startTime; Level = 1, 2, 3 } -ErrorAction SilentlyContinue |
            Select-Object -First 30 TimeCreated, Id, LevelDisplayName, Message |
            Format-List
    }
} *>&1 | Out-File -LiteralPath $reportPath -Encoding utf8 -Width 300
