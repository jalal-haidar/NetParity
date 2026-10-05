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
    private readonly LatencySampler? _latency;
    private readonly LatencyStatsTracker _latencyStats = new();
    private readonly Lock _latencyGate = new();
    private readonly SpeedUnit _unitMode;

    private CancellationTokenSource? _cancellation;
    private Task? _loop;
    private int _probing;
    private long _nextProbeTimestamp;
    private LatencyStats _latestLatency = LatencyStats.Unavailable;

    public MetricsService(SpeedUnit unitMode = SpeedUnit.Auto, string? latencyHost = null, bool enableLatency = true)
    {
        _unitMode = unitMode;
        _latency = enableLatency ? new LatencySampler() : null;
        _latency?.SetTarget(latencyHost ?? "1.1.1.1", 443);
    }

    public event EventHandler<SystemMetrics>? MetricsUpdated;

    public bool CpuAvailable => _cpu.IsAvailable;

    public void SetLatencyTarget(string host, int port) => _latency?.SetTarget(host, port);

    public void Start()
    {
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
                var metrics = Sample();
                MetricsUpdated?.Invoke(this, metrics);
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
            latency = _latestLatency;
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
        if (_latency is null)
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

        _ = Task.Run(async () =>
        {
            try
            {
                var reading = await _latency.MeasureAsync(LatencyTimeoutMs, cancellationToken).ConfigureAwait(false);
                var stats = _latencyStats.Record(
                    reading is not null,
                    reading?.RoundTripMs,
                    _latency.Host,
                    reading?.Method ?? LatencyProbeMethod.None);

                lock (_latencyGate)
                {
                    _latestLatency = stats;
                }
            }
            catch (Exception)
            {
                // A failed probe reports as loss, which is itself useful signal.
                var stats = _latencyStats.Record(false, null, _latency.Host, LatencyProbeMethod.None);
                lock (_latencyGate)
                {
                    _latestLatency = stats;
                }
            }
            finally
            {
                Volatile.Write(ref _probing, 0);
            }
        }, CancellationToken.None);
    }

    public void Dispose()
    {
        _cancellation?.Cancel();

        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // Loop was cancelled mid-await.
        }

        _cancellation?.Dispose();
        _network.Dispose();
        _latency?.Dispose();
    }
}
