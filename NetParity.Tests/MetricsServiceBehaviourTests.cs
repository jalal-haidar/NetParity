using Microsoft.VisualStudio.TestTools.UnitTesting;
using NetParity.Core;
using NetParity.Core.Models;

namespace NetParity.Tests;

/// <summary>
/// Covers the four defects found in review of the C# rewrite. Each test names the bug it
/// prevents from returning, because all four were plausible-looking code that read as
/// correct and would not have shown up in ordinary use.
/// </summary>
[TestClass]
public sealed class MetricsServiceBehaviourTests
{
    private const int SettleMs = 2500;

    /// <summary>
    /// The service used to construct its latency sampler only when latency was enabled at
    /// startup, so a user who launched with the overlay off and switched it on got nothing
    /// until they restarted. Enabling at runtime must start probing.
    /// </summary>
    [TestMethod]
    public void EnablingLatencyAtRuntimeStartsProbing()
    {
        using var service = new MetricsService(latencyEnabled: false);

        var before = service.ProbeCount;
        service.Start();
        Thread.Sleep(SettleMs);
        var whileDisabled = service.ProbeCount;

        service.LatencyEnabled = true;
        Thread.Sleep(SettleMs);

        Assert.AreEqual(0, before, "Probing must not start before Start is called.");
        Assert.AreEqual(0, whileDisabled, "A disabled service must not send probes.");
        Assert.IsTrue(
            service.ProbeCount > whileDisabled,
            $"Enabling latency at runtime started no probes (count stayed at {whileDisabled}).");
    }

    /// <summary>
    /// Disabling the latency menu item previously only hid the number while probes kept
    /// firing. The README promised no traffic when the overlay was off, so this asserts the
    /// promise instead of trusting it.
    /// </summary>
    [TestMethod]
    public void DisablingLatencyStopsProbing()
    {
        using var service = new MetricsService(latencyEnabled: true);

        service.Start();
        Thread.Sleep(SettleMs);

        var whileEnabled = service.ProbeCount;
        Assert.IsTrue(whileEnabled > 0, "Latency was enabled but nothing was ever probed.");

        service.LatencyEnabled = false;

        // A probe that read the flag as true just before it was cleared still increments,
        // so give that window a moment to close before asserting nothing new starts.
        Thread.Sleep(500);
        var afterDisable = service.ProbeCount;
        Thread.Sleep(SettleMs);

        Assert.AreEqual(
            afterDisable,
            service.ProbeCount,
            "Probes continued after latency was disabled.");
    }

    /// <summary>
    /// Changing unit mode used to dispose and rebuild the whole service from the UI thread.
    /// It is now a property, and the live service must survive the change.
    /// </summary>
    [TestMethod]
    public void UnitModeCanChangeWithoutRestartingTheService()
    {
        using var service = new MetricsService(SpeedUnit.Bits, latencyEnabled: false);

        service.UnitMode = SpeedUnit.Bytes;
        Assert.AreEqual(SpeedUnit.Bytes, service.UnitMode);

        service.UnitMode = SpeedUnit.Auto;
        Assert.AreEqual(SpeedUnit.Auto, service.UnitMode);

        // A restart used to be the only way to pick the new unit up.
        service.Start();
        var updates = 0;
        service.MetricsUpdated += (_, _) => Interlocked.Increment(ref updates);
        Thread.Sleep(SettleMs);

        Assert.IsTrue(updates > 0, "The service stopped delivering metrics after a unit mode change.");
    }

    /// <summary>
    /// Disposal used to race the detached probe task into a disposed Ping. Disposing while
    /// a probe is in flight is the normal exit path, so it must be clean.
    /// </summary>
    [TestMethod]
    public void DisposeIsCleanWhileAProbeIsInFlight()
    {
        var service = new MetricsService(latencyEnabled: true);

        service.Start();
        Thread.Sleep(SettleMs);

        service.Dispose();
        service.Dispose();

        Assert.IsTrue(true, "Dispose completed without throwing while a probe was running.");
    }

    /// <summary>
    /// Calling Start after Dispose previously left a live loop holding disposed samplers.
    /// </summary>
    [TestMethod]
    public void StartAfterDisposeThrows()
    {
        var service = new MetricsService(latencyEnabled: false);
        service.Dispose();

        Assert.ThrowsException<ObjectDisposedException>(service.Start);
    }

    [TestMethod]
    public void LatencyIsReportedAsUnavailableWhenDisabled()
    {
        using var service = new MetricsService(latencyEnabled: false);

        SystemMetrics? latest = null;
        service.MetricsUpdated += (_, metrics) => latest = metrics;
        service.Start();
        Thread.Sleep(SettleMs);

        Assert.IsNotNull(latest);
        Assert.AreEqual(
            LatencyStats.Unavailable,
            latest.Latency,
            "Latency should read as unavailable while it is switched off.");
    }
}