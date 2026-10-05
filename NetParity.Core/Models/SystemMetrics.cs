namespace NetParity.Core.Models;

public enum LatencyProbeMethod
{
    None,
    Icmp,
    Tcp
}

public sealed record LatencyStats
{
    public static readonly LatencyStats Unavailable = new();

    public double? RoundTripMs { get; init; }
    public double? JitterMs { get; init; }
    public double? PacketLossPercent { get; init; }
    public string Target { get; init; } = string.Empty;
    public LatencyProbeMethod Method { get; init; } = LatencyProbeMethod.None;

    public bool IsAvailable => RoundTripMs.HasValue;
}

public sealed record SystemMetrics
{
    public double CpuPercent { get; init; }
    public double RamPercent { get; init; }
    public FormattedSpeed Download { get; init; }
    public FormattedSpeed Upload { get; init; }
    public LatencyStats Latency { get; init; } = LatencyStats.Unavailable;
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
}
