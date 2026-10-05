using NetParity.Core.Models;

namespace NetParity.Core.Metrics;

/// <summary>
/// Derives jitter and packet loss from a stream of probe outcomes.
/// </summary>
/// <remarks>
/// Jitter is the thing that predicts stutter; bandwidth does not. A link can hold a
/// steady 80 Mbps and still drop frames every time round-trip time swings. Tracking
/// the mean absolute deviation between consecutive round trips is what separates a
/// healthy link from a congested one.
/// </remarks>
public sealed class LatencyStatsTracker
{
    private const double JitterAlpha = 0.2;

    private readonly Queue<bool> _window = new();
    private readonly int _windowSize;

    private double _previousRoundTrip;
    private double _jitter;
    private int _jitterDeviations;
    private int _consecutiveLosses;
    private bool _hasPrevious;

    public LatencyStatsTracker(int windowSize = 30)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(windowSize, 2);
        _windowSize = windowSize;
    }

    public LatencyStats Record(bool succeeded, double? roundTripMs, string target, LatencyProbeMethod method)
    {
        // A probe that reports success but hands back an unusable reading is a loss.
        // Counting it as one keeps a stale round-trip time from masquerading as live data.
        var usable = succeeded && roundTripMs is { } reading && double.IsFinite(reading);

        _window.Enqueue(usable);
        while (_window.Count > _windowSize)
        {
            _window.Dequeue();
        }

        if (usable)
        {
            var roundTrip = roundTripMs!.Value;

            if (_hasPrevious)
            {
                var deviation = Math.Abs(roundTrip - _previousRoundTrip);
                _jitter = _jitter + (deviation - _jitter) * JitterAlpha;
                _jitterDeviations++;
            }

            _previousRoundTrip = roundTrip;
            _hasPrevious = true;
            _consecutiveLosses = 0;
        }
        else
        {
            _consecutiveLosses++;
        }

        var lost = _window.Count(x => !x);

        return new LatencyStats
        {
            RoundTripMs = usable ? _previousRoundTrip : null,
            JitterMs = _jitterDeviations > 0 ? _jitter : null,
            PacketLossPercent = lost * 100d / _window.Count,
            Target = target,
            Method = method
        };
    }

    /// <summary>True once losses in a row suggest the target is unreachable, not merely lossy.</summary>
    public bool IsUnreachable => _consecutiveLosses >= 5;
}
