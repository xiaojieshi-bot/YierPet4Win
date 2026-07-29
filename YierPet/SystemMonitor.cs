using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;

namespace YierPet;

/// <summary>CPU, memory, battery, and disk metrics via public Windows APIs.</summary>
public sealed class SystemMonitor
{
    public sealed class BatteryState
    {
        public required int Percent { get; init; }
        public required bool IsCharging { get; init; }
        public required bool OnACPower { get; init; }
    }

    private PerformanceCounter? _cpuCounter;
    private double _lastCpuSample;
    private bool _cpuPrimed;

    public bool MemoryPressureCritical { get; private set; }
    public bool MemoryPressureWarning { get; private set; }

    public void PrimeCpuCounter()
    {
        try
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _ = _cpuCounter.NextValue();
        }
        catch
        {
            _cpuCounter = null;
        }
    }

    /// <summary>Total CPU usage 0…1 since previous call.</summary>
    public double CpuUsage()
    {
        if (_cpuCounter == null)
        {
            PrimeCpuCounter();
            if (_cpuCounter == null) return 0;
        }

        try
        {
            var v = _cpuCounter.NextValue() / 100.0;
            if (!_cpuPrimed)
            {
                _cpuPrimed = true;
                _lastCpuSample = v;
                return 0;
            }
            _lastCpuSample = v;
            return Math.Clamp(v, 0, 1);
        }
        catch
        {
            return _lastCpuSample;
        }
    }

    public void SampleMemoryPressure()
    {
        try
        {
            var status = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (!GlobalMemoryStatusEx(ref status)) return;
            MemoryPressureWarning = status.dwMemoryLoad >= 85;
            MemoryPressureCritical = status.dwMemoryLoad >= 92;
        }
        catch
        {
            MemoryPressureWarning = false;
            MemoryPressureCritical = false;
        }
    }

    /// <summary>null on desktops without a battery.</summary>
    public BatteryState? BatteryState()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT EstimatedChargeRemaining, BatteryStatus FROM Win32_Battery");
            foreach (var obj in searcher.Get())
            {
                var percent = Convert.ToInt32(obj["EstimatedChargeRemaining"]);
                var status = Convert.ToInt32(obj["BatteryStatus"]);
                // 1=discharging, 2=AC, 3=fully charged, 6=charging, etc.
                var onAc = status is 2 or 3 or 6 or 7 or 8 or 9;
                var charging = status is 6 or 7 or 8 or 9;
                return new BatteryState
                {
                    Percent = percent,
                    IsCharging = charging,
                    OnACPower = onAc,
                };
            }
        }
        catch
        {
            // no battery
        }
        return null;
    }

    public (double ratio, double freeGB)? DiskFree()
    {
        try
        {
            var drive = DriveInfo.GetDrives()
                .FirstOrDefault(d => d.IsReady && d.Name.StartsWith(
                    Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\",
                    StringComparison.OrdinalIgnoreCase));
            if (drive == null || drive.TotalSize <= 0) return null;
            var free = (double)drive.AvailableFreeSpace;
            var total = (double)drive.TotalSize;
            return (free / total, free / 1_000_000_000);
        }
        catch
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
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
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);
}
