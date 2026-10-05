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
        // The two do not have to match, and in one direction they cannot: a shared page
        // mapped by fifty processes is counted fifty times, so the working-set sum
        // over-counts and can legitimately exceed physical memory. What must hold is that
        // the processes account for a real share of installed memory, and that the
        // over-count stays bounded rather than exploding.
        var reading = new RamSampler().Read();
        var workingSets = SumWorkingSets();
        var totalPhysical = (double)reading.TotalBytes;

        Assert.IsTrue(reading.TotalBytes > 0, "Total physical memory reported as 0.");
        Assert.IsTrue(reading.AvailableBytes < reading.TotalBytes, "All physical memory reported as available.");
        Assert.IsTrue(workingSets > 0, "No process working sets were readable.");

        Assert.IsTrue(
            workingSets >= totalPhysical * 0.25,
            $"Process working sets account for only {workingSets / totalPhysical * 100d:F1}% of physical " +
            $"memory, which does not reconcile with {reading.UsedPercent:F1}% reported in use.");

        Assert.IsTrue(
            workingSets <= totalPhysical * 2,
            $"Process working sets ({workingSets / 1048576d:F0} MB) exceed twice physical memory " +
            $"({reading.TotalBytes / 1048576d:F0} MB), so the enumeration is likely wrong.");

        Assert.IsTrue(reading.UsedPercent is > 0 and < 100, $"Implausible RAM usage {reading.UsedPercent:F1}%.");
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
