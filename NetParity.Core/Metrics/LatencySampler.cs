using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using NetParity.Core.Models;

namespace NetParity.Core.Metrics;

public readonly record struct LatencyReading(double RoundTripMs, LatencyProbeMethod Method);

/// <summary>
/// Measures round-trip time to a target host, preferring ICMP and falling back to a
/// TCP handshake when ICMP is filtered.
/// </summary>
/// <remarks>
/// The fallback matters more than it looks: plenty of networks, corporate VPNs and
/// some ISPs drop ICMP outright. A tool that only speaks ping reports "no data" on
/// exactly the links users most want to inspect.
/// </remarks>
public sealed class LatencySampler : IDisposable
{
    private const int IcmpFailureThreshold = 3;

    private readonly Ping _ping = new();
    private readonly Lock _gate = new();

    private string _host;
    private int _port;
    private int _consecutiveIcmpFailures;
    private bool _icmpFiltered;

    public LatencySampler(string host = "1.1.1.1", int port = 443)
    {
        _host = host;
        _port = port;
    }

    public string Host
    {
        get
        {
            lock (_gate)
            {
                return _host;
            }
        }
    }

    public LatencyProbeMethod Method
    {
        get
        {
            lock (_gate)
            {
                return _icmpFiltered ? LatencyProbeMethod.Tcp : LatencyProbeMethod.Icmp;
            }
        }
    }

    public void SetTarget(string host, int port)
    {
        lock (_gate)
        {
            _host = host;
            _port = port;
            _consecutiveIcmpFailures = 0;
            _icmpFiltered = false;
        }
    }

    public async Task<LatencyReading?> MeasureAsync(int timeoutMs, CancellationToken cancellationToken)
    {
        string host;
        int port;
        bool useTcp;

        lock (_gate)
        {
            host = _host;
            port = _port;
            useTcp = _icmpFiltered;
        }

        if (!useTcp)
        {
            var reading = await MeasureIcmpAsync(host, timeoutMs, cancellationToken).ConfigureAwait(false);
            if (reading is not null)
            {
                lock (_gate)
                {
                    _consecutiveIcmpFailures = 0;
                }

                return reading;
            }

            bool shouldFallBack;
            lock (_gate)
            {
                _consecutiveIcmpFailures++;
                shouldFallBack = _consecutiveIcmpFailures >= IcmpFailureThreshold && !_icmpFiltered;
                if (shouldFallBack)
                {
                    _icmpFiltered = true;
                }
            }

            if (!shouldFallBack)
            {
                return null;
            }
        }

        return await MeasureTcpAsync(host, port, timeoutMs, cancellationToken).ConfigureAwait(false);
    }

    private async Task<LatencyReading?> MeasureIcmpAsync(string host, int timeoutMs, CancellationToken cancellationToken)
    {
        try
        {
            var reply = await _ping.SendPingAsync(host, timeoutMs).ConfigureAwait(false);
            return reply.Status is IPStatus.Success or IPStatus.TtlExpired
                ? new LatencyReading(reply.RoundtripTime, LatencyProbeMethod.Icmp)
                : null;
        }
        catch (PingException)
        {
            return null;
        }
        catch (SocketException)
        {
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }

    private async Task<LatencyReading?> MeasureTcpAsync(string host, int port, int timeoutMs, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new TcpClient();
            var stopwatch = Stopwatch.StartNew();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(timeoutMs);

            await client.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false);
            stopwatch.Stop();

            return new LatencyReading(stopwatch.Elapsed.TotalMilliseconds, LatencyProbeMethod.Tcp);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (SocketException)
        {
            return null;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }

    public void Dispose() => _ping.Dispose();
}
