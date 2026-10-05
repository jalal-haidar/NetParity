using Microsoft.VisualStudio.TestTools.UnitTesting;
using NetParity.Core.Metrics;

namespace NetParity.Tests;

[TestClass]
public sealed class LatencyStatsTrackerTests
{
    private static readonly string Target = "1.1.1.1";

    [TestMethod]
    public void FirstSuccessReportsRoundTripWithoutJitter()
    {
        var tracker = new LatencyStatsTracker();

        var stats = tracker.Record(succeeded: true, 20d, Target, Core.Models.LatencyProbeMethod.Icmp);

        Assert.AreEqual(20d, stats.RoundTripMs!.Value, 0.0001);
        Assert.IsNull(stats.JitterMs, "Jitter needs two samples before it means anything.");
        Assert.IsTrue(stats.IsAvailable);
    }

    [TestMethod]
    public void SteadyRoundTripConvergesToNearZeroJitter()
    {
        var tracker = new LatencyStatsTracker();

        for (var i = 0; i < 40; i++)
        {
            tracker.Record(succeeded: true, 20d, Target, Core.Models.LatencyProbeMethod.Icmp);
        }

        var stats = tracker.Record(succeeded: true, 20d, Target, Core.Models.LatencyProbeMethod.Icmp);

        Assert.IsTrue(stats.JitterMs < 0.5, $"Steady link reported jitter of {stats.JitterMs}ms.");
    }

    [TestMethod]
    public void AlternatingRoundTripsProduceHighJitter()
    {
        // This is the signal the app exists to surface: throughput can look perfect
        // while round-trip time swings like this.
        var tracker = new LatencyStatsTracker();
        var roundTrip = 10d;
        Core.Models.LatencyStats stats = default!;

        for (var i = 0; i < 40; i++)
        {
            roundTrip = roundTrip == 10 ? 60 : 10;
            stats = tracker.Record(succeeded: true, roundTrip, Target, Core.Models.LatencyProbeMethod.Icmp);
        }

        Assert.IsTrue(stats.JitterMs > 20, $"Oscillating link reported jitter of only {stats.JitterMs}ms.");
    }

    [TestMethod]
    public void PacketLossIsReportedAsPercentageOfWindow()
    {
        var tracker = new LatencyStatsTracker(windowSize: 10);

        for (var i = 0; i < 10; i++)
        {
            tracker.Record(succeeded: false, null, Target, Core.Models.LatencyProbeMethod.Icmp);
        }

        var stats = tracker.Record(succeeded: true, 15d, Target, Core.Models.LatencyProbeMethod.Icmp);

        // The window holds 10 samples: 9 failures plus this success.
        Assert.AreEqual(90d, stats.PacketLossPercent!.Value, 0.0001);
    }

    [TestMethod]
    public void LossClearsRoundTripButRemembersLastGoodValue()
    {
        var tracker = new LatencyStatsTracker();

        tracker.Record(succeeded: true, 18d, Target, Core.Models.LatencyProbeMethod.Icmp);
        var stats = tracker.Record(succeeded: false, null, Target, Core.Models.LatencyProbeMethod.Icmp);

        Assert.IsFalse(stats.IsAvailable, "A lost probe has no current round trip to report.");
        Assert.AreEqual(50d, stats.PacketLossPercent!.Value, 0.0001);
    }

    [TestMethod]
    public void WindowEvictsOldSamples()
    {
        var tracker = new LatencyStatsTracker(windowSize: 4);

        for (var i = 0; i < 10; i++)
        {
            tracker.Record(succeeded: false, null, Target, Core.Models.LatencyProbeMethod.Icmp);
        }

        var stats = tracker.Record(succeeded: true, 12d, Target, Core.Models.LatencyProbeMethod.Icmp);

        // The window holds only the last 4 samples: 3 losses plus this success.
        Assert.AreEqual(75d, stats.PacketLossPercent!.Value, 0.0001);
    }

    [TestMethod]
    public void SustainedLossMarksTargetUnreachable()
    {
        var tracker = new LatencyStatsTracker();

        for (var i = 0; i < 5; i++)
        {
            tracker.Record(succeeded: false, null, Target, Core.Models.LatencyProbeMethod.None);
        }

        Assert.IsTrue(tracker.IsUnreachable);
    }

    [TestMethod]
    public void SuccessResetsUnreachableState()
    {
        var tracker = new LatencyStatsTracker();

        for (var i = 0; i < 6; i++)
        {
            tracker.Record(succeeded: false, null, Target, Core.Models.LatencyProbeMethod.None);
        }

        tracker.Record(succeeded: true, 20d, Target, Core.Models.LatencyProbeMethod.Icmp);

        Assert.IsFalse(tracker.IsUnreachable);
    }

    [TestMethod]
    public void NonFiniteRoundTripIsTreatedAsLoss()
    {
        var tracker = new LatencyStatsTracker();

        tracker.Record(succeeded: true, 20d, Target, Core.Models.LatencyProbeMethod.Icmp);
        var stats = tracker.Record(succeeded: true, double.NaN, Target, Core.Models.LatencyProbeMethod.Icmp);

        Assert.IsFalse(stats.IsAvailable);
    }

    [TestMethod]
    public void JitterAndLossStayBoundedAcrossLongRuns()
    {
        var tracker = new LatencyStatsTracker(windowSize: 30);
        var random = new Random(1234);

        for (var i = 0; i < 500; i++)
        {
            var success = random.NextDouble() > 0.15;
            var stats = tracker.Record(success, success ? random.NextDouble() * 120 : null, Target, Core.Models.LatencyProbeMethod.Icmp);

            Assert.IsTrue(stats.JitterMs is null or >= 0);
            Assert.IsTrue(stats.PacketLossPercent is >= 0 and <= 100);
        }
    }
}
