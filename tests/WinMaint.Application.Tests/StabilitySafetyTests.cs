using WinMaint.Application;
using WinMaint.Domain;

namespace WinMaint.Application.Tests;

public sealed class StabilitySafetyTests
{
    [Fact]
    public async Task NoSafetySourceCannotStartIntensiveProbe()
    {
        var probe = new Probe("cpu");
        var result = await Run([probe], null);
        Assert.Equal(0, probe.Calls);
        Assert.Equal(StabilityProbeStatus.Skipped, Assert.Single(result.Probes).Status);
    }

    [Fact]
    public async Task EachIntensiveStageReceivesFreshResourcesNotInitialBaseline()
    {
        var cpu = new Probe("cpu");
        var memory = new Probe("memory");
        var source = new Source();
        source.Reports.Enqueue(Report(2L * 1024 * 1024 * 1024));
        source.Reports.Enqueue(Report(1024L * 1024 * 1024));
        await Run([cpu, memory], source);
        Assert.Equal(2, source.Refreshes);
        Assert.Equal(2L * 1024 * 1024 * 1024, cpu.FreshMemory);
        Assert.Equal(1024L * 1024 * 1024, memory.FreshMemory);
        Assert.Equal(1, memory.Calls);
    }

    [Fact]
    public async Task ReducedMemoryBeforeStagePreventsAllLaterIntensiveCalls()
    {
        var cpu = new Probe("cpu");
        var memory = new Probe("memory");
        var source = new Source();
        source.Reports.Enqueue(Report(128 * 1024 * 1024));
        var result = await Run([cpu, memory], source);
        Assert.Equal(0, cpu.Calls + memory.Calls);
        Assert.All(result.Probes, probe => Assert.Equal(StabilityProbeStatus.Skipped, probe.Status));
        Assert.Empty(result.Findings);
    }

    [Theory]
    [InlineData("ram")]
    [InlineData("power")]
    [InlineData("thermal")]
    [InlineData("thermal-lost")]
    [InlineData("telemetry-lost")]
    [InlineData("telemetry-error")]
    public async Task RuntimeSafetyLossCancelsAndJoinsActiveProbeAndSkipsNextStage(string failure)
    {
        var active = new Probe("cpu", waitForCancellation: true);
        var next = new Probe("memory");
        var source = new Source { AfterStart = active.Started.Task, Failure = failure };
        var result = await Run([active, next], source);
        Assert.Equal(1, active.Calls);
        Assert.True(active.Stopped);
        Assert.Equal(0, next.Calls);
        Assert.True(source.Samples >= 2);
        Assert.Equal(StabilityProbeStatus.Skipped, result.Probes[0].Status);
        Assert.Contains("seguridad", result.Probes[0].Detail);
        Assert.False(result.WasInterrupted);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task UnavailableWheaBeforeCannotPermitLoadOrProduceLoadConfidence()
    {
        var before = new Probe("hardware-events-before", status: StabilityProbeStatus.Skipped);
        var cpu = new Probe("cpu");
        var result = await Run([before, cpu], new Source());
        Assert.Equal(0, cpu.Calls);
        Assert.Equal(0, HealthRuleEngine.Evaluate(Report(), result).Domains
            .Single(domain => domain.Domain == HealthDomain.LoadStability).ConfidencePercent);
    }

    [Fact]
    public async Task UserCancellationJoinsObserverAndWorkloadAndRetainsInterruptedSession()
    {
        using var cancelled = new CancellationTokenSource();
        var probe = new Probe("cpu", waitForCancellation: true, onStart: cancelled.Cancel);
        var store = new Store();
        var coordinator = new StabilityTestCoordinator([probe], store, StabilityTestOptions.Standard,
            new Source(), TimeSpan.FromMilliseconds(1));
        var result = await coordinator.RunAsync(Report(), HealthRuleEngine.Evaluate(Report()), cancellationToken: cancelled.Token);
        Assert.True(result.WasInterrupted);
        Assert.True(probe.Stopped);
        Assert.False(store.Completed);
    }

    [Fact]
    public async Task ObservedWorkloadFailureIsNotHiddenByConcurrentSafetyStop()
    {
        var active = new Probe("memory", waitForCancellation: true, failureOnStop: true);
        var result = await Run([active], new Source { AfterStart = active.Started.Task, Failure = "power" });
        Assert.Equal(StabilityProbeStatus.Failed, Assert.Single(result.Probes).Status);
        Assert.Contains(result.Findings, finding => finding.Priority == Priority.P0);
        Assert.True(active.Stopped);
    }

    [Fact]
    public async Task MemoryBudgetUsesLowerRuntimeAvailabilityEvenAfterFreshDiagnostic()
    {
        var memory = new Probe("memory");
        var result = await Run([memory], new Source { InitialBytes = 536_870_912 });
        Assert.Equal(107_374_182, memory.Budget);
        Assert.Equal(107_374_182, result.Probes[0].Metrics["safety.memory.budget.bytes"]);
    }

    [Fact]
    public async Task TimeoutStopsObserverAndWorkloadAndSkipsFurtherLoad()
    {
        var cpu = new Probe("cpu", waitForCancellation: true, timeoutMilliseconds: 30);
        var memory = new Probe("memory");
        var result = await Run([cpu, memory], new Source());
        Assert.Equal(StabilityProbeStatus.TimedOut, result.Probes[0].Status);
        Assert.True(cpu.Stopped);
        Assert.Equal(0, memory.Calls);
    }

    [Theory]
    [InlineData(0, true, 0)]
    [InlineData(1, true, 33)]
    [InlineData(2, true, 67)]
    [InlineData(3, true, 100)]
    [InlineData(3, false, 0)]
    public void LoadConfidenceRequiresIntensiveStagesAndComparableWhea(int completed, bool hasBefore, int expected)
    {
        var results = new List<StabilityProbeResult>
        {
            FakeResult("hardware-events-before", hasBefore ? StabilityProbeStatus.Passed : StabilityProbeStatus.Skipped),
            FakeResult("hardware-events-after", StabilityProbeStatus.Passed),
            FakeResult("baseline", StabilityProbeStatus.Passed), FakeResult("cooldown", StabilityProbeStatus.Passed),
        };
        var ids = new[] { "cpu", "memory", "storage-read" };
        for (var index = 0; index < ids.Length; index++)
            results.Add(FakeResult(ids[index], index < completed ? StabilityProbeStatus.Passed : StabilityProbeStatus.Skipped));
        var test = new StabilityTestResult(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
            new([], true), results, false, false, []);
        Assert.Equal(expected, HealthRuleEngine.Evaluate(Report(), test).Domains
            .Single(domain => domain.Domain == HealthDomain.LoadStability).ConfidencePercent);
    }

    private static StabilityProbeResult FakeResult(string id, StabilityProbeStatus status) =>
        new(id, id, status, TimeSpan.Zero, "Synthetic evidence", new Dictionary<string, double>());

    private static async Task<StabilityTestResult> Run(IStabilityProbe[] probes, IStabilitySafetySource? source)
    {
        var baseline = Report();
        var coordinator = new StabilityTestCoordinator(probes, new Store(), StabilityTestOptions.Standard,
            source, TimeSpan.FromMilliseconds(1));
        return await coordinator.RunAsync(baseline, HealthRuleEngine.Evaluate(baseline));
    }

    private static DiagnosticReport Report(long available = 4L * 1024 * 1024 * 1024) => new(DateTimeOffset.UtcNow,
        [new("fixture", "Synthetic resources", EvidenceCoverage.Complete,
        [
            Datum("power.source", text: "Corriente alterna"), Datum("storage.disk.health", text: "Healthy"),
            Datum("storage.disk.operational", text: "OK"), Datum("storage.volume.free.percent", 50),
            Datum("performance.cpu.percent", 10), Datum("memory.available.bytes", available),
            Datum("events.whea.error.count", 0), Datum("events.whea.unknown.count", 0),
        ], [])], true);

    private static DiagnosticDatum Datum(string key, double? value = null, string? text = null) =>
        DiagnosticDatum.Available(key, text ?? "synthetic", "fake", metricKey: key, numericValue: value, textValue: text);

    private sealed class Probe(string id, bool waitForCancellation = false,
        StabilityProbeStatus status = StabilityProbeStatus.Passed, Action? onStart = null,
        bool failureOnStop = false, int timeoutMilliseconds = 5000) : IStabilityProbe
    {
        public string Id => id;
        public string Label => "Fake " + id;
        public TimeSpan Timeout => TimeSpan.FromMilliseconds(timeoutMilliseconds);
        public int Calls { get; private set; }
        public bool Stopped { get; private set; }
        public double? FreshMemory { get; private set; }
        public long? Budget { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<StabilityProbeResult> ExecuteAsync(StabilityProbeContext context, CancellationToken cancellationToken)
        {
            Calls++;
            Budget = context.StageMemoryBudgetBytes;
            FreshMemory = context.StageBaseline?.Sections.SelectMany(section => section.Data)
                .Single(datum => datum.MetricKey == "memory.available.bytes").NumericValue;
            Started.TrySetResult();
            onStart?.Invoke();
            try
            {
                if (waitForCancellation) await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken);
                return new(Id, Label, status, TimeSpan.Zero, "No hardware accessed", new Dictionary<string, double>());
            }
            catch (OperationCanceledException) when (failureOnStop)
            {
                return new(Id, Label, StabilityProbeStatus.Failed, TimeSpan.Zero, "Synthetic observed mismatch", new Dictionary<string, double>());
            }
            finally { Stopped = true; }
        }
    }

    private sealed class Source : IStabilitySafetySource
    {
        public Queue<DiagnosticReport> Reports { get; } = [];
        public int Refreshes { get; private set; }
        public int Samples { get; private set; }
        public Task? AfterStart { get; init; }
        public string? Failure { get; init; }
        public long InitialBytes { get; init; } = 4L * 1024 * 1024 * 1024;
        public Task<DiagnosticReport> RefreshAsync(CancellationToken cancellationToken)
        {
            Refreshes++;
            return Task.FromResult(Reports.Count == 0 ? Report() : Reports.Dequeue());
        }
        public async Task<StabilityRuntimeSample> SampleAsync(CancellationToken cancellationToken)
        {
            Samples++;
            if (Samples == 1 || Failure is null) return new(InitialBytes, true, Failure == "thermal-lost" ? false : null);
            if (AfterStart is not null) await AfterStart.WaitAsync(cancellationToken);
            return Failure switch
            {
                "ram" => new(128 * 1024 * 1024, true, null),
                "power" => new(4L * 1024 * 1024 * 1024, false, null),
                "thermal" => new(4L * 1024 * 1024 * 1024, true, true),
                "thermal-lost" => new(4L * 1024 * 1024 * 1024, true, null),
                "telemetry-error" => throw new IOException("Synthetic telemetry loss"),
                _ => new(null, null, null),
            };
        }
    }

    private sealed class Store : IStabilitySessionStore
    {
        public bool Completed { get; private set; }
        public Task<InterruptedStabilitySession?> ReadInterruptedAsync(CancellationToken cancellationToken) => Task.FromResult<InterruptedStabilitySession?>(null);
        public Task StartAsync(DateTimeOffset startedAt, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task UpdateAsync(string completedProbeId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CompleteAsync(CancellationToken cancellationToken) { Completed = true; return Task.CompletedTask; }
    }
}
