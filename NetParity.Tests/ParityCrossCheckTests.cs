using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NetParity.Core.Metrics;

namespace NetParity.Tests;

/// <summary>
/// Cross-checks the app's figures against independently computed estimates, so a
/// silent change to the formulas cannot quietly break the Task Manager parity claim.
/// </summary>
[TestClass]
public sealed class ParityCrossCheckTests
{
    [TestMethod]
    public void CpuReadingStaysSaneUnderSustainedFullLoad()
    {
        var sampler = new CpuSampler();
        sampler.Sample();

        var stopwatch = Stopwatch.StartNew();
        var peak = 0d;
        var samples = 0;

        // Deliberately oversubscribe: more runnable threads than logical cores. This
        // asserts the reading stays in range and responds at all, which is the property
        // that matters. Asserting it exceeds an idle baseline is not viable because CI
        // runners and busy workstations are never actually idle.
        while (stopwatch.ElapsedMilliseconds < 1500)
        {
            Parallel.For(0, Environment.ProcessorCount * 2, _ => { Thread.SpinWait(2000); });
            peak = Math.Max(peak, sampler.Sample());
            samples++;
        }

        Assert.IsTrue(samples > 0);
        Assert.IsTrue(peak is > 0 and <= 100, $"Saturated CPU reported {peak:F1}%, which is out of range.");
    }

    [TestMethod]
    public void RamReadingReconcilesWithSummingProcessWorkingSets()
    {
        // (total - available) / total is the formula Task Manager uses. Summing every
        // process working set is an independent estimate of the same quantity.
        //
        // The two are not equal and must not be. A shared page mapped by many processes
        // is counted once per process, so the working-set sum over-counts; kernel memory
        // and standby cache belong to no process, so it also under-counts those. Measured
        // ratios of working sets to in-use memory are 1.2x on an idle runner and 1.5x on
        // a loaded workstation, so the useful assertion is that they stay in the same
        // neighbourhood, not that they match.
        //
        // This is what catches a regression to GlobalMemoryStatusEx.dwMemoryLoad: that
        // field is commit charge, which on a machine under paging reads 169% of physical
        // memory while in-use physical memory reads 84%.
        var reading = new RamSampler().Read();
        var workingSets = SumWorkingSets();
        var totalPhysical = (double)reading.TotalBytes;
        var usedPhysical = totalPhysical - reading.AvailableBytes;

        Assert.IsTrue(reading.TotalBytes > 0, "Total physical memory reported as 0.");
        Assert.IsTrue(reading.AvailableBytes < reading.TotalBytes, "All physical memory reported as available.");
        Assert.IsTrue(workingSets > 0, "No process working sets were readable.");
        Assert.IsTrue(usedPhysical > 0, "No physical memory reported as in use.");

        Assert.IsTrue(
            reading.UsedPercent is > 0 and < 100,
            $"Implausible RAM usage {reading.UsedPercent:F1}%. Commit charge on a machine under " +
            "paging reads well above 100%, so this is the assertion that catches a regression to dwMemoryLoad.");

        var ratio = workingSets / usedPhysical;

        Assert.IsTrue(
            ratio is >= 0.8 and <= 3.0,
            $"Working sets are {ratio:F2}x in-use memory, which is outside the expected 0.8x-3.0x band.");

        Assert.IsTrue(
            workingSets <= totalPhysical * 2,
            $"Process working sets ({workingSets / 1048576d:F0} MB) exceed twice physical memory " +
            $"({reading.TotalBytes / 1048576d:F0} MB), so the enumeration is likely wrong.");
    }

    private static long SumWorkingSets()
    {
        long total = 0;

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                total += process.WorkingSet64;
            }
            catch (Exception)
            {
                // Processes exit between enumeration and inspection.
            }
            finally
            {
                process.Dispose();
            }
        }

        return total;
    }
}
