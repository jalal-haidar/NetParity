using System.Diagnostics;
using System.Threading;
using NetParity.Core.Metrics;
using NetParity.Core.Models;

namespace NetParity.Core;

/// <summary>
/// Owns the single sampling loop that drives every reading.
/// </summary>
/// <remarks>
/// An earlier revision ran CPU, RAM and network on three independent timers that all
/// mutated shared fields and posted to the UI thread, which produced torn reads and
/// three blocking dispatcher hops per sample. One loop, one immutable snapshot, one
/// event removes all of it.
/// </remarks>
public sealed class MetricsService : IDisposable
{
    private const int SampleIntervalMs = 250;
    private const int LatencyProbeIntervalMs = 1000;
    private const int LatencyTimeoutMs = 1200;

    private readonly CpuSampler _cpu = new();
    private readonly RamSampler _ram = new();
    private readonly NetworkSampler _network = new();
    private readonly LatencySampler _latency;
    private readonly LatencyStatsTracker _latencyStats = new();
    private readonly Lock _latencyGate = new();

    private CancellationTokenSource? _cancellation;
    private Task? _loop;
    private Task? _probe;
    private volatile SpeedUnit _unitMode;
    private volatile bool _latencyEnabled;
    private volatile bool _disposed;
    private int _probing;
    private long _nextProbeTimestamp;
    private long _probeCount;
    private LatencyStats _latestLatency = LatencyStats.Unavailable;

    public MetricsService(SpeedUnit unitMode = SpeedUnit.Auto, string? latencyHost = null, bool latencyEnabled = true)
    {
        _unitMode = unitMode;
        _latencyEnabled = latencyEnabled;
        _latency = new LatencySampler();
        _latency.SetTarget(latencyHost ?? "1.1.1.1", 443);
    }

    public event EventHandler<SystemMetrics>? MetricsUpdated;

    public bool CpuAvailable => _cpu.IsAvailable;

    /// <summary>
    /// How throughput is presented. Settable at any time, because the alternative was
    /// tearing down and rebuilding this service from the UI thread just to change a label.
    /// </summary>
    public SpeedUnit UnitMode
    {
        get => _unitMode;
        set => _unitMode = value;
    }

    /// <summary>
    /// Whether latency probes are sent. Turning this off stops the probes entirely rather
    /// than merely hiding the result, so a user who switches the overlay off is not still
    /// generating traffic to a third party.
    /// </summary>
    public bool LatencyEnabled
    {
        get => _latencyEnabled;
        set => _latencyEnabled = value;
    }

    /// <summary>
    /// Number of latency probes started. Exposed so tests can assert that nothing is being
    /// sent, which is otherwise impossible to verify without capturing packets.
    /// </summary>
    public long ProbeCount => Interlocked.Read(ref _probeCount);

    public void SetLatencyTarget(string host, int port) => _latency.SetTarget(host, port);

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_loop is not null)
        {
            return;
        }

        _cancellation = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_cancellation.Token));
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(SampleIntervalMs));

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                MetricsUpdated?.Invoke(this, Sample());
                BeginLatencyProbe(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
    }

    private SystemMetrics Sample()
    {
        var network = _network.Sample();

        LatencyStats latency;
        lock (_latencyGate)
        {
            latency = _latencyEnabled ? _latestLatency : LatencyStats.Unavailable;
        }

        return new SystemMetrics
        {
            CpuPercent = _cpu.Sample(),
            RamPercent = _ram.Sample(),
            Download = UnitFormatter.Format(network.BitsPerSecondDown, _unitMode),
            Upload = UnitFormatter.Format(network.BitsPerSecondUp, _unitMode),
            Latency = latency,
            TimestampUtc = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Probes latency off the sampling loop so a one-second probe timeout cannot stall
    /// CPU, RAM and network updates.
    /// </summary>
    /// <remarks>
    /// The interval gate matters. Without it the probe fires on every 250 ms sample, which
    /// means four probes a second: measurably costly for a tool whose pitch is that it
    /// costs nothing, and rude to the host being probed. Once a second is both
    /// sustainable and fast enough for jitter to mean anything.
    /// </remarks>
    private void BeginLatencyProbe(CancellationToken cancellationToken)
    {
        if (!_latencyEnabled || _disposed)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _probing, 1, 0) != 0)
        {
            return;
        }

        var now = Stopwatch.GetTimestamp();
        if (now < Interlocked.Read(ref _nextProbeTimestamp))
        {
            Volatile.Write(ref _probing, 0);
            return;
        }

        Interlocked.Exchange(
            ref _nextProbeTimestamp,
            now + (Stopwatch.Frequency * LatencyProbeIntervalMs / 1000));

        Interlocked.Increment(ref _probeCount);

        // The task is retained so Dispose can wait for it. Without that, disposal raced
        // the probe into a disposed Ping.
        _probe = Task.Run(async () =>
        {
            try
            {
                var reading = await _latency.MeasureAsync(LatencyTimeoutMs, cancellationToken).ConfigureAwait(false);
                Record(reading is not null, reading?.RoundTripMs, reading?.Method ?? LatencyProbeMethod.None);
            }
            catch (Exception)
            {
                // A failed probe reports as loss, which is itself useful signal.
                Record(succeeded: false, roundTripMs: null, LatencyProbeMethod.None);
            }
            finally
            {
                Volatile.Write(ref _probing, 0);
            }
        }, CancellationToken.None);
    }

    private void Record(bool succeeded, double? roundTripMs, LatencyProbeMethod method)
    {
        var stats = _latencyStats.Record(succeeded, roundTripMs, _latency.Host, method);

        lock (_latencyGate)
        {
            _latestLatency = stats;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cancellation?.Cancel();

        WaitFor(_loop);
        // Wait for the in-flight probe before disposing the sampler it is using.
        WaitFor(_probe);

        _cancellation?.Dispose();
        _network.Dispose();
        _latency.Dispose();
    }

    private static void WaitFor(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            task.Wait(TimeSpan.FromSeconds(3));
        }
        catch (AggregateException)
        {
            // Loop or probe was cancelled mid-await.
        }
    }
}
