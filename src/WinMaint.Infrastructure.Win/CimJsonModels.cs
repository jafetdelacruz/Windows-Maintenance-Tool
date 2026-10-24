using System.Text.Json;

namespace WinMaint.Infrastructure.Win;

internal sealed record MemoryModuleInfo(
    string? BankLabel,
    string? DeviceLocator,
    string? Manufacturer,
    ulong? Capacity,
    uint? Speed,
    uint? ConfiguredClockSpeed);

internal sealed record PhysicalDiskInfo(
    string? FriendlyName,
    string? MediaType,
    string? BusType,
    string? HealthStatus,
    string? OperationalStatus,
    ulong? Size,
    int? Temperature,
    int? Wear,
    long? PowerOnHours,
    long? ReadErrorsTotal,
    long? WriteErrorsTotal,
    bool ReliabilityAvailable);

internal static class CimJsonParser
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    internal static IReadOnlyList<MemoryModuleInfo> ParseMemoryModules(string json) =>
        ParseArray<MemoryModuleInfo>(json);

    internal static IReadOnlyList<PhysicalDiskInfo> ParsePhysicalDisks(string json) =>
        ParseArray<PhysicalDiskInfo>(json);

    private static List<T> ParseArray<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        var result = JsonSerializer.Deserialize<List<T>>(json, Options);
        return result ?? [];
    }
}
