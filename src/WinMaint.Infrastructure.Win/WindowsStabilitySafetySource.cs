using WinMaint.Application;

namespace WinMaint.Infrastructure.Win;

internal sealed class WindowsStabilitySafetySource : IStabilitySafetySource
{
    public Task<DiagnosticReport> RefreshAsync(CancellationToken cancellationToken) =>
        WindowsDiagnosticComposition.Create().CollectAsync(cancellationToken);

    public Task<StabilityRuntimeSample> SampleAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var memory = MemoryStatusEx.Create();
        long? available = NativeMethods.GlobalMemoryStatusEx(ref memory) && memory.AvailablePhysical <= long.MaxValue
            ? (long)memory.AvailablePhysical : null;
        bool? power = null;
        if (NativeMethods.GetSystemPowerStatus(out var status))
            power = status.AcLineStatus switch
            {
                1 => true,
                0 when status.BatteryLifePercent <= 100 => status.BatteryLifePercent >= 40,
                _ => null,
            };
        // Windows does not provide a universal reliable thermal threshold for these workloads.
        return Task.FromResult(new StabilityRuntimeSample(available, power, ReliableThermalCritical: null));
    }
}
