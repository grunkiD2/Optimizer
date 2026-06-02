using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Text.Json;

using Optimizer.WinUI.Helpers;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public class SystemMonitorService : IDisposable
{
    private static readonly string HistoryPath = AppPaths.GetDataFile("history.json");

    private readonly ConcurrentQueue<SystemResource> _history;
    private readonly int _maxHistorySize = 1000;
    private bool _isMonitoring;
    private int _sampleCount;
    private CancellationTokenSource? _monitorCancellation;

    // Persistent rate counters (disk/network need to live across samples to report a rate).
    private readonly object _counterGate = new();
    private PerformanceCounter? _cpuCounter;
    private PerformanceCounter? _diskReadCounter;
    private PerformanceCounter? _diskWriteCounter;
    private List<PerformanceCounter>? _netRecvCounters;
    private List<PerformanceCounter>? _netSentCounters;
    private List<PerformanceCounter>? _coreCounters;

    public TimeSpan SampleInterval { get; set; } = TimeSpan.FromSeconds(1);

    public int CurrentHistorySize => _history.Count;

    public SystemMonitorService()
    {
        _history = new ConcurrentQueue<SystemResource>();
        LoadHistory();
    }

    public async Task StartMonitoringAsync(int sampleDurationSeconds = 3600)
    {
        if (_isMonitoring)
            return;

        await Task.Run(async () =>
        {
            try
            {
                _monitorCancellation = new CancellationTokenSource();
                var token = _monitorCancellation.Token;

                _isMonitoring = true;
                var stopwatch = Stopwatch.StartNew();

                while (_isMonitoring && !token.IsCancellationRequested)
                {
                    var duration = stopwatch.Elapsed.TotalSeconds;
                    if (duration >= sampleDurationSeconds)
                        break;

                    try
                    {
                        var resourceMetrics = await CollectCurrentMetricsAsync();
                        _history.Enqueue(resourceMetrics);

                        while (_history.Count > _maxHistorySize)
                        {
                            _history.TryDequeue(out _);
                        }

                        if (++_sampleCount % 60 == 0)
                        {
                            SaveHistory();
                        }
                    }
                    catch (Exception ex)
                    {
                        EngineLog.Write($"Error collecting metrics in monitoring loop: {ex.Message}");
                    }

                    await Task.Delay((int)SampleInterval.TotalMilliseconds, token);
                }
            }
            catch (OperationCanceledException)
            {
                EngineLog.Write("Monitoring was cancelled");
            }
            catch (Exception ex)
            {
                EngineLog.Write($"Error in monitoring task: {ex.Message}");
            }
            finally
            {
                _isMonitoring = false;
            }
        });
    }

    public void StopMonitoring()
    {
        _isMonitoring = false;

        // Idempotent: null out before disposing so a second call (e.g. from Dispose) is safe.
        var cts = _monitorCancellation;
        _monitorCancellation = null;
        if (cts != null)
        {
            try { cts.Cancel(); } catch (ObjectDisposedException) { }
            cts.Dispose();
        }

        SaveHistory();
    }

    private void LoadHistory()
    {
        try
        {
            if (!File.Exists(HistoryPath)) return;
            var loaded = JsonSerializer.Deserialize<List<SystemResource>>(File.ReadAllText(HistoryPath));
            if (loaded != null)
            {
                foreach (var sample in loaded.TakeLast(_maxHistorySize))
                {
                    _history.Enqueue(sample);
                }
            }
        }
        catch (Exception ex)
        {
            EngineLog.Write($"Error loading history: {ex.Message}");
        }
    }

    public void SaveHistory()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(HistoryPath)!);
            var snapshot = _history.ToArray();
            File.WriteAllText(HistoryPath, JsonSerializer.Serialize(snapshot));
        }
        catch (Exception ex)
        {
            EngineLog.Write($"Error saving history: {ex.Message}");
        }
    }

    public Task<IEnumerable<SystemResource>> GetResourceHistoryAsync(int sampleCount)
    {
        var result = _history.OrderByDescending(x => x.Timestamp)
                           .Take(sampleCount);
        return Task.FromResult(result);
    }

    private async Task<SystemResource> CollectCurrentMetricsAsync()
    {
        return await Task.Run(CollectSnapshot);
    }

    /// <summary>Collects a single, fully-populated metrics snapshot (CPU, memory, GPU, temps, disk, network).</summary>
    public SystemResource CollectSnapshot()
    {
        try
        {
            EnsureRateCounters();
            return new SystemResource
            {
                Timestamp = DateTime.Now,
                CpuUsagePercentage = GetCpuUsage(),
                TotalProcessors = GetProcessorCount(),
                CyclesPerSecond = GetProcessorFrequency(),
                TotalPhysicalMemory = GetTotalPhysicalMemory(),
                AvailablePhysicalMemory = GetAvailableMemory(),
                TotalVirtualMemory = GetTotalVirtualMemorySize(),
                AvailableVirtualMemory = GetAvailableVirtualMemory(),
                GpuUsagePercentage = GetGpuUsage(),
                GpuMemoryUsage = GetGpuMemoryUsage(),
                CpuTemperature = GetCpuTemperature(),
                GpuTemperature = GetGpuTemperature(),
                DiskReadSpeed = ReadCounter(_diskReadCounter),
                DiskWriteSpeed = ReadCounter(_diskWriteCounter),
                NetworkInSpeed = SumCounters(_netRecvCounters),
                NetworkOutSpeed = SumCounters(_netSentCounters)
            };
        }
        catch (Exception ex)
        {
            EngineLog.Write($"Error collecting metrics: {ex.Message}");
            return GetDefaultSystemResource();
        }
    }

    private void EnsureRateCounters()
    {
        lock (_counterGate)
        {
            try
            {
                if (_cpuCounter == null)
                {
                    _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total", true);
                    _cpuCounter.NextValue(); // prime — first call always returns 0
                }

                _diskReadCounter ??= new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total", true);
                _diskWriteCounter ??= new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total", true);

                if (_netRecvCounters == null)
                {
                    _netRecvCounters = new List<PerformanceCounter>();
                    _netSentCounters = new List<PerformanceCounter>();
                    var category = new PerformanceCounterCategory("Network Interface");
                    foreach (var instance in category.GetInstanceNames())
                    {
                        try
                        {
                            _netRecvCounters.Add(new PerformanceCounter("Network Interface", "Bytes Received/sec", instance, true));
                            _netSentCounters.Add(new PerformanceCounter("Network Interface", "Bytes Sent/sec", instance, true));
                        }
                        catch { /* skip interfaces that can't be opened */ }
                    }
                }
            }
            catch (Exception ex)
            {
                EngineLog.Write($"Error initializing rate counters: {ex.Message}");
            }
        }
    }

    /// <summary>Per-logical-core CPU usage (0–100). Uses persistent counters so rates are meaningful.</summary>
    public IReadOnlyList<double> GetPerCoreUsage()
    {
        lock (_counterGate)
        {
            if (_coreCounters == null)
            {
                _coreCounters = new List<PerformanceCounter>();
                try
                {
                    var category = new PerformanceCounterCategory("Processor");
                    var cores = category.GetInstanceNames()
                        .Where(n => n != "_Total" && int.TryParse(n, out _))
                        .OrderBy(n => int.Parse(n));
                    foreach (var core in cores)
                    {
                        try { _coreCounters.Add(new PerformanceCounter("Processor", "% Processor Time", core, true)); }
                        catch { /* skip */ }
                    }
                }
                catch (Exception ex)
                {
                    EngineLog.Write($"Error initializing per-core counters: {ex.Message}");
                }
            }

            return _coreCounters
                .Select(c => { try { return Math.Round(c.NextValue(), 0); } catch { return 0d; } })
                .ToList();
        }
    }

    private static double ReadCounter(PerformanceCounter? counter)
    {
        try { return counter != null ? Math.Round(counter.NextValue(), 0) : 0; }
        catch { return 0; }
    }

    private static double SumCounters(List<PerformanceCounter>? counters)
    {
        if (counters == null) return 0;
        double sum = 0;
        foreach (var c in counters)
        {
            try { sum += c.NextValue(); } catch { /* ignore */ }
        }
        return Math.Round(sum, 0);
    }

    private SystemResource GetDefaultSystemResource()
    {
        return new SystemResource
        {
            Timestamp = DateTime.Now,
            TotalProcessors = Environment.ProcessorCount
        };
    }

    private double GetCpuUsage()
    {
        try
        {
            return Math.Round(_cpuCounter?.NextValue() ?? 0, 2);
        }
        catch (Exception ex)
        {
            EngineLog.Write($"Error getting CPU usage: {ex.Message}");
            return 0.0;
        }
    }

    private int GetProcessorCount()
    {
        return Environment.ProcessorCount;
    }

    private long GetProcessorFrequency()
    {
        try
        {
            using (var freqCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total", true))
            {
                return (long)(freqCounter.NextValue() * 1_000_000);
            }
        }
        catch (Exception ex)
        {
            EngineLog.Write($"Error getting processor frequency: {ex.Message}");
            return 0;
        }
    }

    private long GetTotalPhysicalMemory()
    {
        try
        {
            var wmiQuery = new System.Management.SelectQuery("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
            using (var searcher = new System.Management.ManagementObjectSearcher(wmiQuery))
            {
                var collection = searcher.Get();
                foreach (var obj in collection)
                {
                    if (obj["TotalPhysicalMemory"] != null)
                    {
                        return Convert.ToInt64(obj["TotalPhysicalMemory"]);
                    }
                }
            }
            return GC.GetTotalMemory(false);
        }
        catch (Exception ex)
        {
            EngineLog.Write($"Error getting total physical memory: {ex.Message}");
            return GC.GetTotalMemory(false);
        }
    }

    private long GetAvailableMemory()
    {
        try
        {
            using (var memCounter = new PerformanceCounter("Memory", "Available MBytes"))
            {
                return (long)memCounter.NextValue() * 1024 * 1024;
            }
        }
        catch (Exception ex)
        {
            EngineLog.Write($"Error getting available memory: {ex.Message}");
            return 0;
        }
    }

    private long GetTotalVirtualMemorySize()
    {
        try
        {
            var wmiQuery = new System.Management.SelectQuery("SELECT MaximumSize FROM Win32_PageFileUsage");
            using (var searcher = new System.Management.ManagementObjectSearcher(wmiQuery))
            {
                var collection = searcher.Get();
                long total = 0;
                foreach (var obj in collection)
                {
                    if (obj["MaximumSize"] != null)
                    {
                        total += Convert.ToInt64(obj["MaximumSize"]) * 1024 * 1024;
                    }
                }
                return total;
            }
        }
        catch (Exception ex)
        {
            EngineLog.Write($"Error getting total virtual memory: {ex.Message}");
            return 0;
        }
    }

    private long GetAvailableVirtualMemory()
    {
        try
        {
            // "Available Bytes" reports uncommitted virtual address space (distinct from physical).
            using (var virtualMemCounter = new PerformanceCounter("Memory", "Available Bytes"))
            {
                return (long)virtualMemCounter.NextValue();
            }
        }
        catch (Exception ex)
        {
            EngineLog.Write($"Error getting available virtual memory: {ex.Message}");
            return 0;
        }
    }

    private double GetGpuUsage()
    {
        try
        {
            using (var gpuCounter = new PerformanceCounter("GPU Engine", "Utilization Percentage", "_Total", true))
            {
                return Math.Round(gpuCounter.NextValue(), 2);
            }
        }
        catch (Exception ex)
        {
            EngineLog.Write($"Error getting GPU usage: {ex.Message}");
            return 0.0;
        }
    }

    private long GetGpuMemoryUsage()
    {
        try
        {
            using (var gpuMemCounter = new PerformanceCounter("GPU Memory", "Used Non-Shared Memory", "_Total", true))
            {
                return (long)gpuMemCounter.NextValue();
            }
        }
        catch (Exception ex)
        {
            EngineLog.Write($"Error getting GPU memory usage: {ex.Message}");
            return 0;
        }
    }

    private double GetCpuTemperature()
    {
        try
        {
            var wmiQuery = new System.Management.SelectQuery("SELECT CurrentTemperature FROM Win32_TemperatureProbe WHERE SystemName IS NOT NULL");
            using (var searcher = new System.Management.ManagementObjectSearcher(wmiQuery))
            {
                var collection = searcher.Get();
                foreach (var obj in collection)
                {
                    if (obj["CurrentTemperature"] != null)
                    {
                        return Convert.ToDouble(obj["CurrentTemperature"]) / 10.0;
                    }
                }
            }
            return 0.0;
        }
        catch (Exception ex)
        {
            EngineLog.Write($"Error getting CPU temperature: {ex.Message}");
            return 0.0;
        }
    }

    private double GetGpuTemperature()
    {
        try
        {
            var wmiQuery = new System.Management.SelectQuery("SELECT CurrentTemperature FROM Win32_TemperatureProbe WHERE Description LIKE '%GPU%'");
            using (var searcher = new System.Management.ManagementObjectSearcher(wmiQuery))
            {
                var collection = searcher.Get();
                foreach (var obj in collection)
                {
                    if (obj["CurrentTemperature"] != null)
                    {
                        return Convert.ToDouble(obj["CurrentTemperature"]) / 10.0;
                    }
                }
            }
            return 0.0;
        }
        catch (Exception ex)
        {
            EngineLog.Write($"Error getting GPU temperature: {ex.Message}");
            return 0.0;
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopMonitoring();
            SaveHistory();
            lock (_counterGate)
            {
                _cpuCounter?.Dispose();
                _diskReadCounter?.Dispose();
                _diskWriteCounter?.Dispose();
                _netRecvCounters?.ForEach(c => c.Dispose());
                _netSentCounters?.ForEach(c => c.Dispose());
                _coreCounters?.ForEach(c => c.Dispose());
            }
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
