using System.Net.NetworkInformation;
using System.Text.Json;
using Microsoft.Win32;
using WinMaint.Application;
using WinMaint.Domain;

namespace WinMaint.Infrastructure.Win;

internal sealed record PcManagerAppxPackage(
    string? Name,
    string? Version,
    string? PackageFamilyName,
    string? Status,
    string? ApplicationUserModelId);

internal sealed record PcManagerAppxProbe(
    bool PcManagerQuerySucceeded,
    IReadOnlyList<PcManagerAppxPackage> PcManagerPackages,
    bool StoreQuerySucceeded,
    IReadOnlyList<PcManagerAppxPackage> StorePackages,
    bool WinGetQuerySucceeded,
    string? WinGetPath);

internal static class PcManagerProbeParser
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static PcManagerAppxProbe Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new JsonException("La sonda AppX no devolvió JSON.");
        }

        return JsonSerializer.Deserialize<PcManagerAppxProbe>(json, Options) ??
            throw new JsonException("La sonda AppX devolvió un resultado nulo.");
    }
}

internal static class PcManagerDetectionMapper
{
    public const int MinimumSupportedBuild = 19042;
    public const string SchemaVersion = "1.0";

    public static PcManagerDetectionResult Map(
        PcManagerAppxProbe? probe,
        int operatingSystemBuild,
        bool isWindows,
        bool isX64OperatingSystem,
        StorePolicyState storePolicy,
        bool? networkInterfaceAvailable,
        DateTimeOffset capturedAt)
    {
        var compatible = isWindows &&
            isX64OperatingSystem &&
            operatingSystemBuild >= MinimumSupportedBuild;
        var package = SelectPackage(probe);
        var evidence = new List<string>
        {
            $"Build detectado: {operatingSystemBuild}.",
            isX64OperatingSystem ? "Sistema operativo x64." : "Sistema operativo no x64.",
        };

        var installation = probe?.PcManagerQuerySucceeded switch
        {
            true when package is not null => PcManagerInstallationState.Installed,
            true => PcManagerInstallationState.Absent,
            _ => PcManagerInstallationState.Unknown,
        };
        evidence.Add(installation switch
        {
            PcManagerInstallationState.Installed => "Paquete Microsoft PC Manager detectado para el usuario actual.",
            PcManagerInstallationState.Absent => "No se detectó el paquete Microsoft PC Manager para el usuario actual.",
            _ => "No se pudo determinar el estado del paquete Microsoft PC Manager.",
        });

        var winget = probe?.WinGetQuerySucceeded switch
        {
            true when IsAllowedWinGetPath(probe.WinGetPath) => AvailabilityState.Available,
            true => AvailabilityState.Unavailable,
            _ => AvailabilityState.Unknown,
        };
        var store = probe?.StoreQuerySucceeded switch
        {
            true when probe.StorePackages.Any(item =>
                string.Equals(item.Status, "Ok", StringComparison.OrdinalIgnoreCase)) => AvailabilityState.Available,
            true => AvailabilityState.Unavailable,
            _ => AvailabilityState.Unknown,
        };
        var network = networkInterfaceAvailable switch
        {
            true => AvailabilityState.Available,
            false => AvailabilityState.Unavailable,
            null => AvailabilityState.Unknown,
        };

        return new PcManagerDetectionResult(
            SchemaVersion,
            capturedAt,
            operatingSystemBuild,
            isX64OperatingSystem,
            compatible ? AvailabilityState.Available : AvailabilityState.Unavailable,
            installation,
            package,
            winget,
            store,
            storePolicy,
            network,
            evidence);
    }

    private static PcManagerPackageInfo? SelectPackage(PcManagerAppxProbe? probe)
    {
        var selected = probe?.PcManagerPackages
            .Where(item => !string.IsNullOrWhiteSpace(item.Name) &&
                !string.IsNullOrWhiteSpace(item.Version) &&
                !string.IsNullOrWhiteSpace(item.PackageFamilyName))
            .OrderByDescending(item => ParseVersion(item.Version))
            .FirstOrDefault();
        return selected is null
            ? null
            : new PcManagerPackageInfo(
                selected.Name!,
                selected.Version!,
                selected.PackageFamilyName!,
                selected.ApplicationUserModelId);
    }

    private static Version ParseVersion(string? value) =>
        Version.TryParse(value, out var version) ? version : new Version();

    private static bool IsAllowedWinGetPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            return false;
        }

        return string.Equals(Path.GetFileName(path), "winget.exe", StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class WindowsPcManagerDetector : IPcManagerDetector
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(20);

    private const string ProbeScript = """
        $pcSucceeded = $true
        $storeSucceeded = $true
        $wingetSucceeded = $true
        try {
            $pcPackages = @(Get-AppxPackage -ErrorAction Stop |
                Where-Object { $_.Name -like '*PCManager*' -and $_.PublisherId -eq '8wekyb3d8bbwe' } |
                ForEach-Object {
                    $aumid = $null
                    try {
                        $applicationId = @(Get-AppxPackageManifest -Package $_.PackageFullName -ErrorAction Stop).Package.Applications.Application.Id | Select-Object -First 1
                        if ($applicationId) { $aumid = $_.PackageFamilyName + '!' + $applicationId }
                    } catch {}
                    [pscustomobject]@{
                        Name = $_.Name
                        Version = $_.Version.ToString()
                        PackageFamilyName = $_.PackageFamilyName
                        Status = $_.Status.ToString()
                        ApplicationUserModelId = $aumid
                    }
                })
        } catch {
            $pcSucceeded = $false
            $pcPackages = @()
        }
        try {
            $storePackages = @(Get-AppxPackage -Name 'Microsoft.WindowsStore' -ErrorAction Stop |
                ForEach-Object {
                    [pscustomobject]@{
                        Name = $_.Name
                        Version = $_.Version.ToString()
                        PackageFamilyName = $_.PackageFamilyName
                        Status = $_.Status.ToString()
                        ApplicationUserModelId = $null
                    }
                })
        } catch {
            $storeSucceeded = $false
            $storePackages = @()
        }
        try {
            $wingetPath = (Get-Command 'winget.exe' -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1).Source
        } catch {
            $wingetSucceeded = $false
            $wingetPath = $null
        }
        [pscustomobject]@{
            PcManagerQuerySucceeded = $pcSucceeded
            PcManagerPackages = $pcPackages
            StoreQuerySucceeded = $storeSucceeded
            StorePackages = $storePackages
            WinGetQuerySucceeded = $wingetSucceeded
            WinGetPath = $wingetPath
        } | ConvertTo-Json -Depth 5 -Compress
        """;

    public async Task<PcManagerDetectionResult> DetectAsync(CancellationToken cancellationToken)
    {
        PcManagerAppxProbe? probe = null;
        var result = await PowerShellJsonRunner.RunAsync(
            ProbeScript,
            ProbeTimeout,
            cancellationToken).ConfigureAwait(false);
        if (!result.TimedOut && result.ExitCode == 0)
        {
            try
            {
                probe = PcManagerProbeParser.Parse(result.StandardOutput);
            }
            catch (JsonException)
            {
                probe = null;
            }
        }

        return PcManagerDetectionMapper.Map(
            probe,
            Environment.OSVersion.Version.Build,
            OperatingSystem.IsWindows(),
            Environment.Is64BitOperatingSystem,
            ReadStorePolicy(),
            ReadNetworkInterfaceAvailability(),
            DateTimeOffset.Now);
    }

    private static bool? ReadNetworkInterfaceAvailability()
    {
        try
        {
            return NetworkInterface.GetIsNetworkAvailable();
        }
        catch (NetworkInformationException)
        {
            return null;
        }
    }

    private static StorePolicyState ReadStorePolicy()
    {
        try
        {
            var machineBlocked = IsStoreBlocked(RegistryHive.LocalMachine, RegistryView.Registry64);
            var userBlocked = IsStoreBlocked(RegistryHive.CurrentUser, RegistryView.Default);
            return machineBlocked || userBlocked
                ? StorePolicyState.Blocked
                : StorePolicyState.NotBlockedByKnownPolicy;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return StorePolicyState.Unknown;
        }
    }

    private static bool IsStoreBlocked(RegistryHive hive, RegistryView view)
    {
        using var baseKey = RegistryKey.OpenBaseKey(hive, view);
        using var key = baseKey.OpenSubKey(@"SOFTWARE\Policies\Microsoft\WindowsStore", writable: false);
        return IsOne(key?.GetValue("RemoveWindowsStore")) ||
            IsOne(key?.GetValue("DisableStoreApps")) ||
            IsOne(key?.GetValue("RequirePrivateStoreOnly"));
    }

    private static bool IsOne(object? value) => value is int integer && integer == 1;
}

public static class WindowsPcManagerComposition
{
    public static PcManagerDetectionCoordinator CreateDetector() =>
        new(new WindowsPcManagerDetector());

    public static PcManagerActionCoordinator CreateActionCoordinator(
        bool installExecutionEnabled,
        bool externalLaunchEnabled)
    {
        var detector = new WindowsPcManagerDetector();
        var runner = new WindowsPcManagerActionRunner(
            detector,
            new WindowsPcManagerPlatformGateway());
        return new PcManagerActionCoordinator(
            runner,
            installExecutionEnabled,
            externalLaunchEnabled);
    }
}
