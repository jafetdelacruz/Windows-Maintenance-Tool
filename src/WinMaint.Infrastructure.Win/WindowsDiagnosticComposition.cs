using WinMaint.Application;

namespace WinMaint.Infrastructure.Win;

public static class WindowsDiagnosticComposition
{
    public static ReadOnlyDiagnosticCoordinator Create()
    {
        IReadOnlyDiagnosticProvider[] providers =
        [
            new SystemOverviewProvider(),
            new PerformanceProvider(),
            new MemoryModulesProvider(),
            new PreviousMemoryDiagnosticProvider(),
            new StorageProvider(),
            new WindowsEventProvider(),
            new RestartAndIntegrityProvider(),
        ];

        return new ReadOnlyDiagnosticCoordinator(providers);
    }
}
