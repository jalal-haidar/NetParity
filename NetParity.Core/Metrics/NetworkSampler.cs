using System.Diagnostics;
using System.Net.NetworkInformation;

namespace NetParity.Core.Metrics;

public readonly record struct NetworkSample(double BitsPerSecondDown, double BitsPerSecondUp);

/// <summary>
/// Samples aggregate link throughput in bits per second across physical adapters.
/// </summary>
/// <remarks>
/// Smoothing is an exponential moving average with a time constant rather than a
/// fixed sample count, so the displayed rate is independent of how often the caller
/// happens to sample.
/// </remarks>
public sealed class NetworkSampler : IDisposable
{
    private const double SmoothingSeconds = 1.0;
    private const int MaxInterfaces = 32;
    private static readonly TimeSpan InterfaceRefreshInterval = TimeSpan.FromSeconds(15);

    private readonly Lock _gate = new();

    private NetworkInterface[] _interfaces = [];
    private long _lastReceived;
    private long _lastSent;
    private long _lastTimestamp;
    private bool _primed;
    private DateTime _nextInterfaceRefresh;
    private double _smoothedDown;
    private double _smoothedUp;

    public NetworkSampler()
    {
        RefreshInterfaces();
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
    }

    public IReadOnlyList<string> InterfaceNames
    {
        get
        {
            lock (_gate)
            {
                return _interfaces.Select(ni => ni.Name).ToArray();
            }
        }
    }

    public NetworkSample Sample()
    {
        lock (_gate)
        {
            if (DateTime.UtcNow >= _nextInterfaceRefresh)
            {
                _nextInterfaceRefresh = DateTime.UtcNow + InterfaceRefreshInterval;
                RefreshInterfaces();
            }

            var now = Stopwatch.GetTimestamp();
            var totals = ReadTotals();

            if (!_primed)
            {
                Prime(now, totals);
                return new NetworkSample(_smoothedDown, _smoothedUp);
            }

            var elapsed = (now - _lastTimestamp) / (double)Stopwatch.Frequency;

            // A negative delta means an adapter was reset or re-enumerated, so the
            // baseline is meaningless rather than merely spiky.
            if (elapsed <= 0 || totals.Received < _lastReceived || totals.Sent < _lastSent)
            {
                Prime(now, totals);
                return new NetworkSample(_smoothedDown, _smoothedUp);
            }

            var alpha = 1d - Math.Exp(-elapsed / SmoothingSeconds);
            _smoothedDown = Smooth(_smoothedDown, (totals.Received - _lastReceived) * 8d / elapsed, alpha);
            _smoothedUp = Smooth(_smoothedUp, (totals.Sent - _lastSent) * 8d / elapsed, alpha);

            _lastReceived = totals.Received;
            _lastSent = totals.Sent;
            _lastTimestamp = now;

            return new NetworkSample(_smoothedDown, _smoothedUp);
        }
    }

    private static double Smooth(double current, double next, double alpha) =>
        current + (next - current) * alpha;

    private void Prime(long timestamp, (long Received, long Sent) totals)
    {
        _lastReceived = totals.Received;
        _lastSent = totals.Sent;
        _lastTimestamp = timestamp;
        _primed = true;
        _smoothedDown = 0;
        _smoothedUp = 0;
    }

    private (long Received, long Sent) ReadTotals()
    {
        long received = 0;
        long sent = 0;

        foreach (var adapter in _interfaces)
        {
            try
            {
                var statistics = adapter.GetIPStatistics();
                received += statistics.BytesReceived;
                sent += statistics.BytesSent;
            }
            catch (Exception)
            {
                // An adapter can disappear between enumeration and read. Skip it.
            }
        }

        return (received, sent);
    }

    private void RefreshInterfaces()
    {
        try
        {
            _interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(IsUsable)
                .Take(MaxInterfaces)
                .ToArray();
        }
        catch (Exception)
        {
            _interfaces = [];
        }

        _nextInterfaceRefresh = DateTime.UtcNow + InterfaceRefreshInterval;
        _primed = false;
    }

    private static bool IsUsable(NetworkInterface adapter)
    {
        if (adapter.OperationalStatus != OperationalStatus.Up)
        {
            return false;
        }

        // Loopback double-counts nothing useful, and tunnel adapters duplicate the
        // traffic of the physical link underneath them.
        return adapter.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel);
    }

    private void OnNetworkChanged(object? sender, EventArgs e) => RefreshInterfaces();

    public void Dispose()
    {
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
    }
}
