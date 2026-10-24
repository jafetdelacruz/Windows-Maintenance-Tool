using WinMaint.Domain;

namespace WinMaint.Application;

public interface IPcManagerDetector
{
    Task<PcManagerDetectionResult> DetectAsync(CancellationToken cancellationToken);
}

public sealed class PcManagerDetectionCoordinator(IPcManagerDetector detector)
{
    public Task<PcManagerDetectionResult> DetectAsync(CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(detector);
        return detector.DetectAsync(cancellationToken);
    }
}
