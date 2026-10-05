using Microsoft.VisualStudio.TestTools.UnitTesting;
using NetParity.Core.Metrics;

namespace NetParity.Tests;

[TestClass]
public sealed class CpuSamplerTests
{
    [TestMethod]
    public void AllIdleTimeReadsZeroPercent()
    {
        Assert.AreEqual(0d, CpuSampler.CalculateBusyPercent(idleDelta: 1000, totalDelta: 1000), 0.0001);
    }

    [TestMethod]
    public void NoIdleTimeReadsOneHundredPercent()
    {
        Assert.AreEqual(100d, CpuSampler.CalculateBusyPercent(idleDelta: 0, totalDelta: 1000), 0.0001);
    }

    [TestMethod]
    public void HalfIdleReadsFiftyPercent()
    {
        Assert.AreEqual(50d, CpuSampler.CalculateBusyPercent(idleDelta: 500, totalDelta: 1000), 0.0001);
    }

    [TestMethod]
    public void QuarterIdleReadsSeventyFivePercent()
    {
        Assert.AreEqual(75d, CpuSampler.CalculateBusyPercent(idleDelta: 250, totalDelta: 1000), 0.0001);
    }

    [TestMethod]
    public void FirstSampleIsZeroRatherThanGarbage()
    {
        var sampler = new CpuSampler();

        Assert.AreEqual(0d, sampler.Sample(), 0.0001, "The first read has no baseline to diff against.");
    }

    [TestMethod]
    public void SustainedLoadReadsHighButBoundedPercent()
    {
        var sampler = new CpuSampler();

        var busiest = 0d;
        for (var i = 0; i < 20; i++)
        {
            Thread.Sleep(50);
            var sample = sampler.Sample();
            Assert.IsTrue(sample is >= 0 and <= 100, $"CPU sample {sample} escaped the 0-100 range.");
            busiest = Math.Max(busiest, sample);
        }

        Assert.IsTrue(busiest > 0, "Expected some non-zero CPU reading while sampling.");
    }

    [TestMethod]
    public void ZeroTotalDeltaReturnsZero()
    {
        Assert.AreEqual(0d, CpuSampler.CalculateBusyPercent(0, 0));
    }

    [TestMethod]
    public void NegativeIdleDeltaIsTreatedAsFullyBusyRatherThanNegativePercent()
    {
        // Counters can reset underneath us. Never surface a negative percentage.
        var result = CpuSampler.CalculateBusyPercent(idleDelta: -500, totalDelta: 1000);

        Assert.AreEqual(100d, result, 0.0001);
    }

    [TestMethod]
    [DataRow(double.NaN)]
    [DataRow(double.PositiveInfinity)]
    public void NonFiniteTotalsReturnZero(double total)
    {
        Assert.AreEqual(0d, CpuSampler.CalculateBusyPercent(100, total));
    }
}
