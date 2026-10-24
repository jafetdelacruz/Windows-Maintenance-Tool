using WinMaint.Application;
using WinMaint.Domain;
using WinMaint.Infrastructure.Win;

namespace WinMaint.Windows.Tests;

public sealed class StabilityProbeEvidenceTests
{
    [Theory]
    [InlineData(false, StabilityProbeStatus.Skipped)]
    [InlineData(true, StabilityProbeStatus.Passed)]
    public async Task LaterWheaQueryNeedsARealEarlierMark(bool hasEarlierMark, StabilityProbeStatus expected)
    {
        var context = Context();
        if (hasEarlierMark) context.SharedMetrics["whea.latest.before"] = 0;
        var probe = new HardwareEventStabilityProbe("hardware-events-after", "Fake WHEA", "whea.latest.after",
            "whea.latest.before", _ => Task.FromResult(new ControlledProcessResult(0, "", "", false)));
        var result = await probe.ExecuteAsync(context, CancellationToken.None);
        Assert.Equal(expected, result.Status);
        Assert.True(context.SharedMetrics.ContainsKey("whea.latest.after"));
        Assert.Equal(hasEarlierMark, context.SharedMetrics.ContainsKey("whea.latest.before"));
    }

    [Theory]
    [InlineData("", StabilityProbeStatus.Skipped)]
    [InlineData("<Level>2</Level>", StabilityProbeStatus.Failed)]
    [InlineData("<Level>4</Level>", StabilityProbeStatus.Passed)]
    public async Task WheaUnknownNewErrorAndInformationalStayDistinct(string level, StabilityProbeStatus expected)
    {
        var raw = new byte[128];
        "CPER"u8.CopyTo(raw);
        Array.Fill(raw, (byte)255, 6, 4);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(12, 4), 3);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(20, 4), 128);
        var xml = "<Event xmlns=\"http://schemas.microsoft.com/win/2004/08/events/event\"><System><Provider Name=\"Microsoft-Windows-WHEA-Logger\"/><EventID>17</EventID>" +
            level + "<TimeCreated SystemTime=\"2026-10-21T00:00:00Z\"/></System><EventData><Data Name=\"RawData\">" +
            Convert.ToHexString(raw) + "</Data></EventData></Event>";
        var context = Context();
        context.SharedMetrics["before"] = 0;
        var probe = new HardwareEventStabilityProbe("after", "Fake", "after", "before",
            _ => Task.FromResult(new ControlledProcessResult(0, xml, "", false)));
        var result = await probe.ExecuteAsync(context, CancellationToken.None);
        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public async Task UnavailableBeforeAndAvailableAfterDoesNotCreateAnApprovedComparison()
    {
        var context = Context();
        var before = new HardwareEventStabilityProbe("before", "Fake", "before", queryRunner:
            _ => Task.FromResult(new ControlledProcessResult(1, "", "", false)));
        var after = new HardwareEventStabilityProbe("after", "Fake", "after", "before",
            _ => Task.FromResult(new ControlledProcessResult(0, "", "", false)));
        Assert.Equal(StabilityProbeStatus.Skipped, (await before.ExecuteAsync(context, CancellationToken.None)).Status);
        Assert.Equal(StabilityProbeStatus.Skipped, (await after.ExecuteAsync(context, CancellationToken.None)).Status);
    }

    [Theory]
    [InlineData(1073741824d, 214748364L)]
    [InlineData(8589934592d, 1073741824L)]
    [InlineData(double.NaN, 0L)]
    [InlineData(-1d, 0L)]
    public void MemoryBudgetIsBoundedByRecentAvailableValue(double available, long expected)
    {
        var report = new DiagnosticReport(DateTimeOffset.UtcNow, [new("fresh", "Fresh", EvidenceCoverage.Complete,
            [DiagnosticDatum.Available("RAM", "fake", "fake", metricKey: "memory.available.bytes", numericValue: available)], [])], true);
        Assert.Equal(expected, MemoryPatternProbe.CalculateTarget(report));
        Assert.Equal(0, MemoryPatternProbe.CalculateTarget(null));
    }

    [Fact]
    public async Task CpuCompletionDoesNotClaimArithmeticValidation()
    {
        // Zero duration: exercises result construction without generating CPU work.
        var result = await new CpuStabilityProbe(TimeSpan.Zero).ExecuteAsync(Context(), CancellationToken.None);
        Assert.Contains("no se validó exactitud aritmética", result.Detail);
        Assert.DoesNotContain("sin error de cálculo", result.Detail);
        Assert.Equal(0, result.Metrics["cpu.iterations"]);
        Assert.Equal(StabilityProbeStatus.Skipped, result.Status);
    }

    private static StabilityProbeContext Context() => new(new(DateTimeOffset.UnixEpoch, [], true), StabilityTestOptions.Standard);

    [Fact]
    public async Task NonemptyUninterpretableWheaOutputDoesNotBecomeEmptyHealthySample()
    {
        var context = Context();
        var probe = new HardwareEventStabilityProbe("before", "Fake", "before", queryRunner:
            _ => Task.FromResult(new ControlledProcessResult(0, "<Unexpected/>", "", false)));
        var result = await probe.ExecuteAsync(context, CancellationToken.None);
        Assert.Equal(StabilityProbeStatus.Skipped, result.Status);
        Assert.False(context.SharedMetrics.ContainsKey("before"));
    }

    [Fact]
    public async Task InvalidBeforeMarkDoesNotBecomeApprovedComparison()
    {
        var context = Context();
        context.SharedMetrics["before"] = double.NaN;
        var probe = new HardwareEventStabilityProbe("after", "Fake", "after", "before",
            _ => Task.FromResult(new ControlledProcessResult(0, "", "", false)));
        Assert.Equal(StabilityProbeStatus.Skipped, (await probe.ExecuteAsync(context, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task ReadOnlyNativeRuntimeSampleDoesNotInventThermalAvailability()
    {
        var sample = await new WindowsStabilitySafetySource().SampleAsync(CancellationToken.None);
        Assert.Null(sample.ReliableThermalCritical);
        if (sample.AvailableMemoryBytes.HasValue) Assert.True(sample.AvailableMemoryBytes.Value >= 0);
    }
}
