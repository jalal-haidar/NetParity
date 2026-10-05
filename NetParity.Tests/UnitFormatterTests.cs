using Microsoft.VisualStudio.TestTools.UnitTesting;
using NetParity.Core;

namespace NetParity.Tests;

[TestClass]
public sealed class UnitFormatterTests
{
    [TestMethod]
    [DataRow(0d, "bps", 0d)]
    [DataRow(999d, "bps", 999d)]
    [DataRow(1000d, "Kbps", 1d)]
    [DataRow(1500d, "Kbps", 1.5d)]
    [DataRow(999_999d, "Kbps", 999.999d)]
    [DataRow(1_000_000d, "Mbps", 1d)]
    [DataRow(94_500_000d, "Mbps", 94.5d)]
    [DataRow(1_000_000_000d, "Gbps", 1d)]
    [DataRow(2_400_000_000d, "Gbps", 2.4d)]
    public void FormatBitsUsesDecimalSteps(double bits, string expectedUnit, double expectedValue)
    {
        var result = UnitFormatter.Format(bits);

        Assert.AreEqual(expectedUnit, result.Unit);
        Assert.AreEqual(expectedValue, result.Value, 0.0001);
    }

    [TestMethod]
    public void FormatUsesDecimalNotBinarySteps()
    {
        // 1024 bits must read as 1.024 Kbps, not 1 Kbps. This is the exact bug the
        // original implementation shipped with.
        var result = UnitFormatter.Format(1024);

        Assert.AreEqual("Kbps", result.Unit);
        Assert.AreEqual(1.024, result.Value, 0.0001);
    }

    [TestMethod]
    public void FormatBytesModeConvertsBitsToBytes()
    {
        var result = UnitFormatter.Format(8_000_000, SpeedUnit.Bytes);

        Assert.AreEqual("MB/s", result.Unit);
        Assert.AreEqual(1d, result.Value, 0.0001);
    }

    [TestMethod]
    public void FormatBytesModeUsesByteUnitsBelowKilo()
    {
        var result = UnitFormatter.Format(400, SpeedUnit.Bytes);

        Assert.AreEqual("B/s", result.Unit);
        Assert.AreEqual(50d, result.Value, 0.0001);
    }

    [TestMethod]
    [DataRow(-5000d)]
    [DataRow(double.NaN)]
    [DataRow(double.PositiveInfinity)]
    [DataRow(double.NegativeInfinity)]
    public void FormatClampsInvalidInputToZero(double bits)
    {
        var result = UnitFormatter.Format(bits);

        Assert.AreEqual(0d, result.Value);
    }

    [TestMethod]
    public void FormatNeverExceedsGigaBits()
    {
        var result = UnitFormatter.Format(1_000_000_000_000d);

        Assert.AreEqual("Gbps", result.Unit);
        Assert.AreEqual(1000d, result.Value, 0.0001);
    }
}
