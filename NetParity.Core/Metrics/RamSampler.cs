using System.Runtime.InteropServices;

namespace NetParity.Core.Metrics;

/// <summary>
/// Samples physical memory in use as (total - available) / total, which is the basis
/// Task Manager uses.
/// </summary>
/// <remarks>
/// This deliberately ignores GlobalMemoryStatusEx.dwMemoryLoad. That field reports
/// commit charge against the page file, so it disagrees with Task Manager whenever
/// paging is involved -- commonly showing near 100% while physical RAM is half empty.
/// </remarks>
public readonly record struct RamReading(ulong TotalBytes, ulong AvailableBytes)
{
    public double UsedPercent => TotalBytes == 0
        ? 0
        : Math.Clamp((TotalBytes - AvailableBytes) * 100d / TotalBytes, 0, 100);
}

public sealed class RamSampler
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    public double Sample() => Read().UsedPercent;

    public RamReading Read()
    {
        var status = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };

        if (!GlobalMemoryStatusEx(ref status) || status.ullTotalPhys == 0)
        {
            return new RamReading(0, 0);
        }

        return new RamReading(status.ullTotalPhys, status.ullAvailPhys);
    }
}
