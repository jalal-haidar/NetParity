using System.Runtime.InteropServices;

namespace NetParity.Core.Metrics;

/// <summary>
/// Samples CPU utilisation as the share of processor time spent non-idle, which is
/// the definition Task Manager uses.
/// </summary>
/// <remarks>
/// This reads <c>GetSystemTimes</c> rather than a performance counter. Two reasons.
/// The "% Processor Utility" counter folds in frequency and turbo scaling, so it can
/// exceed 100% and clamping it hides exactly the load spikes this app exists to
/// reveal; "% Processor Time" is correct but needs a package reference that bloats
/// the single-file publish. Kernel time from this API includes idle time, which is
/// why idle is subtracted from the kernel+user total rather than added.
/// </remarks>
public sealed class CpuSampler
{
    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

    private ulong _lastIdle;
    private ulong _lastKernel;
    private ulong _lastUser;
    private bool _primed;

    public bool IsAvailable { get; private set; } = true;

    public double Sample()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
        {
            IsAvailable = false;
            return 0;
        }

        var idleTicks = ToUInt64(idle);
        var kernelTicks = ToUInt64(kernel);
        var userTicks = ToUInt64(user);

        if (!_primed)
        {
            _lastIdle = idleTicks;
            _lastKernel = kernelTicks;
            _lastUser = userTicks;
            _primed = true;
            return 0;
        }

        var idleDelta = idleTicks - _lastIdle;
        var totalDelta = (kernelTicks - _lastKernel) + (userTicks - _lastUser);

        _lastIdle = idleTicks;
        _lastKernel = kernelTicks;
        _lastUser = userTicks;

        return CalculateBusyPercent(idleDelta, totalDelta);
    }

    /// <summary>
    /// Busy percentage from deltas accumulated since the previous sample.
    /// </summary>
    public static double CalculateBusyPercent(double idleDelta, double totalDelta)
    {
        if (!double.IsFinite(totalDelta) || totalDelta <= 0)
        {
            return 0;
        }

        if (!double.IsFinite(idleDelta) || idleDelta < 0)
        {
            idleDelta = 0;
        }

        var busyPercent = (totalDelta - idleDelta) * 100d / totalDelta;
        return Math.Clamp(busyPercent, 0, 100);
    }

    private static ulong ToUInt64(FileTime value) => ((ulong)value.High << 32) | value.Low;
}
