namespace NetParity.Core;

public enum SpeedUnit
{
    Auto,
    Bits,
    Bytes
}

public readonly record struct FormattedSpeed(double Value, string Unit)
{
    public override string ToString() => $"{Value:0.##} {Unit}";
}

/// <summary>
/// Formats throughput using decimal (SI) multiples.
/// </summary>
/// <remarks>
/// Windows and ISPs both report throughput in powers of 1000, so Kbps here means
/// 1000 bits per second. Scaling by 1024 instead would misreport every sub-megabit
/// figure by 2.4%.
/// </remarks>
public static class UnitFormatter
{
    private const double Kilo = 1_000d;
    private const double Mega = 1_000_000d;
    private const double Giga = 1_000_000_000d;

    public static FormattedSpeed Format(double bitsPerSecond, SpeedUnit mode = SpeedUnit.Auto)
    {
        var value = double.IsFinite(bitsPerSecond) ? Math.Max(0, bitsPerSecond) : 0;
        var asBytes = mode == SpeedUnit.Bytes;

        // Bytes mode presents the same rate in bytes; both forms then scale in
        // decimal steps of 1000.
        var scaled = asBytes ? value / 8d : value;

        var (kiloUnit, megaUnit, gigaUnit) = asBytes
            ? ("KB/s", "MB/s", "GB/s")
            : ("Kbps", "Mbps", "Gbps");

        if (scaled >= Giga) return new FormattedSpeed(scaled / Giga, gigaUnit);
        if (scaled >= Mega) return new FormattedSpeed(scaled / Mega, megaUnit);
        if (scaled >= Kilo) return new FormattedSpeed(scaled / Kilo, kiloUnit);
        return new FormattedSpeed(scaled, asBytes ? "B/s" : "bps");
    }
}
