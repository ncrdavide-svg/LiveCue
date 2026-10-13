using System.Diagnostics;
using System.Runtime.InteropServices;
using ComTypes = System.Runtime.InteropServices.ComTypes;

namespace LiveCue.Core.Services;

public record SystemStats(
    double CpuPercent,
    double GpuPercent,
    double RamUsedGb,
    double RamTotalGb,
    double RamPercent,
    double AppCpuPercent,
    double AppRamMb)
{
    /// <summary>True when GPU usage couldn't be read (no "GPU Engine" performance counters on this system/driver).</summary>
    public bool GpuAvailable => GpuPercent >= 0;
}

/// <summary>
/// Lightweight sampler for overall system load (CPU/RAM/GPU) and this process's own usage,
/// meant to be polled roughly once a second so the user can see if LiveCue is overloading
/// the machine during a show. GPU reading relies on Windows' "GPU Engine" performance
/// counter category and is best-effort (not every driver exposes it).
/// </summary>
public class SystemMonitor
{
    private bool _hasPrevSystemTimes;
    private ulong _prevIdleTicks, _prevKernelTicks, _prevUserTicks;

    private bool _hasPrevAppSample;
    private DateTime _prevAppSampleTimeUtc;
    private TimeSpan _prevAppCpuTime;

    private readonly List<PerformanceCounter> _gpuCounters = new();
    private DateTime _lastGpuRefreshUtc = DateTime.MinValue;
    private bool _gpuCategoryMissing;

    public SystemStats Sample()
    {
        var (usedGb, totalGb, ramPercent) = SampleRam();
        var (appCpu, appRamMb) = SampleApp();
        return new SystemStats(
            CpuPercent: SampleCpu(),
            GpuPercent: SampleGpu(),
            RamUsedGb: usedGb,
            RamTotalGb: totalGb,
            RamPercent: ramPercent,
            AppCpuPercent: appCpu,
            AppRamMb: appRamMb);
    }

    private double SampleCpu()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
            return 0;

        ulong idleTicks = ToTicks(idle), kernelTicks = ToTicks(kernel), userTicks = ToTicks(user);

        if (!_hasPrevSystemTimes)
        {
            _prevIdleTicks = idleTicks;
            _prevKernelTicks = kernelTicks;
            _prevUserTicks = userTicks;
            _hasPrevSystemTimes = true;
            return 0;
        }

        ulong idleDelta = idleTicks - _prevIdleTicks;
        // kernelTicks already includes idle time, per the Win32 contract of GetSystemTimes.
        ulong totalDelta = (kernelTicks - _prevKernelTicks) + (userTicks - _prevUserTicks);

        _prevIdleTicks = idleTicks;
        _prevKernelTicks = kernelTicks;
        _prevUserTicks = userTicks;

        if (totalDelta == 0) return 0;
        return Math.Clamp((1.0 - (double)idleDelta / totalDelta) * 100.0, 0, 100);
    }

    private (double usedGb, double totalGb, double percent) SampleRam()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref status)) return (0, 0, 0);

        const double bytesPerGb = 1024.0 * 1024 * 1024;
        double totalGb = status.ullTotalPhys / bytesPerGb;
        double availGb = status.ullAvailPhys / bytesPerGb;
        return (totalGb - availGb, totalGb, status.dwMemoryLoad);
    }

    private (double cpuPercent, double ramMb) SampleApp()
    {
        using var process = Process.GetCurrentProcess();
        var nowUtc = DateTime.UtcNow;
        var cpuTime = process.TotalProcessorTime;
        double ramMb = process.WorkingSet64 / (1024.0 * 1024);

        if (!_hasPrevAppSample)
        {
            _prevAppSampleTimeUtc = nowUtc;
            _prevAppCpuTime = cpuTime;
            _hasPrevAppSample = true;
            return (0, ramMb);
        }

        double wallMs = (nowUtc - _prevAppSampleTimeUtc).TotalMilliseconds;
        double cpuMs = (cpuTime - _prevAppCpuTime).TotalMilliseconds;
        _prevAppSampleTimeUtc = nowUtc;
        _prevAppCpuTime = cpuTime;

        if (wallMs <= 0) return (0, ramMb);
        double percent = cpuMs / (wallMs * Environment.ProcessorCount) * 100.0;
        return (Math.Clamp(percent, 0, 100), ramMb);
    }

    /// <returns>0-100, or -1 if GPU usage isn't readable on this system.</returns>
    private double SampleGpu()
    {
        if (_gpuCategoryMissing) return -1;

        try
        {
            RefreshGpuCountersIfStale();
            if (_gpuCounters.Count == 0) return _gpuCategoryMissing ? -1 : 0;

            double total = 0;
            foreach (var counter in _gpuCounters)
            {
                try { total += counter.NextValue(); }
                catch { /* instance may have disappeared between refreshes */ }
            }
            return Math.Clamp(total, 0, 100);
        }
        catch
        {
            _gpuCategoryMissing = true;
            return -1;
        }
    }

    private void RefreshGpuCountersIfStale()
    {
        if (_gpuCounters.Count > 0 && DateTime.UtcNow - _lastGpuRefreshUtc < TimeSpan.FromSeconds(5))
            return;

        _lastGpuRefreshUtc = DateTime.UtcNow;

        foreach (var counter in _gpuCounters) counter.Dispose();
        _gpuCounters.Clear();

        if (!PerformanceCounterCategory.Exists("GPU Engine"))
        {
            _gpuCategoryMissing = true;
            return;
        }

        var category = new PerformanceCounterCategory("GPU Engine");
        foreach (var instanceName in category.GetInstanceNames())
        {
            // Engine instances are named like "pid_1234_luid_..._phys_0_eng_0_engtype_3D";
            // only count the 3D engine so the total isn't inflated by decode/copy/video engines.
            if (!instanceName.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase)) continue;

            foreach (var counter in category.GetCounters(instanceName))
            {
                if (counter.CounterName == "Utilization Percentage")
                    _gpuCounters.Add(counter);
                else
                    counter.Dispose();
            }
        }
    }

    private static ulong ToTicks(ComTypes.FILETIME ft) => ((ulong)(uint)ft.dwHighDateTime << 32) | (uint)ft.dwLowDateTime;

    // ---- P/Invoke ----

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
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
    private static extern bool GetSystemTimes(out ComTypes.FILETIME lpIdleTime, out ComTypes.FILETIME lpKernelTime, out ComTypes.FILETIME lpUserTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
}
