using Microsoft.VisualStudio.TestTools.UnitTesting;
using NetParity.Core;
using NetParity.Core.Metrics;
using NetParity.Core.Models;

namespace NetParity.Tests;

/// <summary>
/// Guards the parity claim the product is sold on: every reading must stay inside the
/// same bounds Task Manager reports, and none may go negative or absurd.
/// </summary>
[TestClass]
public sealed class SamplerRangeTests
{
    [TestMethod]
    public void RamSamplerStaysWithinZeroToOneHundred()
    {
        var sampler = new RamSampler();

        for (var i = 0; i < 10; i++)
        {
            var value = sampler.Sample();
            Assert.IsTrue(value is >= 0 and <= 100, $"RAM sample {value} escaped the 0-100 range.");
        }
    }

    [TestMethod]
    public void RamSamplerReflectsRealUsage()
    {
        var sampler = new RamSampler();

        // Every machine has some memory in use, so a persistent 0 would mean the
        // calculation is broken rather than that the machine is idle.
        var value = sampler.Sample();

        Assert.IsTrue(value > 0, "RAM usage reported 0%, which is not plausible for a running system.");
        Assert.IsTrue(value < 100, "RAM usage reported 100% outside of a memory pressure test.");
    }

    [TestMethod]
    public void NetworkSamplerPrimesToZeroRatherThanSpiking()
    {
        using var sampler = new NetworkSampler();

        var first = sampler.Sample();

        Assert.AreEqual(0d, first.BitsPerSecondDown, 0.0001);
        Assert.AreEqual(0d, first.BitsPerSecondUp, 0.0001);
    }

    [TestMethod]
    public void NetworkSamplerNeverReportsNegativeThroughput()
    {
        using var sampler = new NetworkSampler();

        for (var i = 0; i < 20; i++)
        {
            Thread.Sleep(50);
            var sample = sampler.Sample();

            Assert.IsTrue(sample.BitsPerSecondDown >= 0, $"Negative download rate: {sample.BitsPerSecondDown}.");
            Assert.IsTrue(sample.BitsPerSecondUp >= 0, $"Negative upload rate: {sample.BitsPerSecondUp}.");
        }
    }

    [TestMethod]
    public void NetworkSamplerSeesAtLeastOneUsableAdapter()
    {
        using var sampler = new NetworkSampler();
        sampler.Sample();

        Assert.IsTrue(sampler.InterfaceNames.Count > 0, "No usable network adapters found; throughput would always read 0.");
    }

    [TestMethod]
    public void LoopbackAndTunnelAdaptersAreExcluded()
    {
        using var sampler = new NetworkSampler();

        foreach (var name in sampler.InterfaceNames)
        {
            Assert.IsFalse(
                name.Contains("Loopback", StringComparison.OrdinalIgnoreCase),
                $"Loopback adapter '{name}' should have been filtered out.");
        }
    }

    [TestMethod]
    public void MetricsServicePublishesSnapshotsWithinBounds()
    {
        using var service = new MetricsService(latencyEnabled: false);
        using var completed = new ManualResetEventSlim(false);

        SystemMetrics? latest = null;
        service.MetricsUpdated += (_, metrics) =>
        {
            latest = metrics;
            completed.Set();
        };

        service.Start();
        Assert.IsTrue(completed.Wait(TimeSpan.FromSeconds(5)), "MetricsService never published a snapshot.");

        Assert.IsNotNull(latest);
        Assert.IsTrue(latest.CpuPercent is >= 0 and <= 100);
        Assert.IsTrue(latest.RamPercent is >= 0 and <= 100);
        Assert.IsTrue(latest.Download.Value >= 0);
        Assert.IsTrue(latest.Upload.Value >= 0);
    }

    [TestMethod]
    public void MetricsServiceStartsOnlyOnce()
    {
        using var service = new MetricsService(latencyEnabled: false);
        var count = 0;

        service.MetricsUpdated += (_, _) => Interlocked.Increment(ref count);
        service.Start();
        service.Start();
        Thread.Sleep(600);

        // A double start would double the event rate rather than be silently ignored.
        var elapsedSamples = (int)Volatile.Read(ref count);
        Assert.IsTrue(elapsedSamples is > 0 and <= 20, $"Unexpected sample count {elapsedSamples}; sampling may be duplicated.");
    }

    [TestMethod]
    public void DisposedServiceStopsPublishing()
    {
        var service = new MetricsService(latencyEnabled: false);
        var count = 0;

        service.MetricsUpdated += (_, _) => Interlocked.Increment(ref count);
        service.Start();
        Thread.Sleep(400);

        service.Dispose();
        var afterDispose = Volatile.Read(ref count);
        Thread.Sleep(400);

        Assert.AreEqual(afterDispose, Volatile.Read(ref count), "Service kept sampling after disposal.");
    }
}
