using System.Diagnostics;

namespace Optimizer.WinUI.Services;

public class StressTestService : IStressTestService
{
    private readonly ISensorService _sensors;
    private CancellationTokenSource? _cts;
    private readonly StressTestStatus _status = new();

    public StressTestStatus Status => _status;
    public event Action? StatusChanged;

    public bool   IsPrime95Installed   { get; private set; }
    public bool   IsCinebenchInstalled { get; private set; }
    private string? _prime95Path;
    private string? _cinebenchPath;

    public StressTestService(ISensorService sensors)
    {
        _sensors = sensors;
        DetectTools();
    }

    // ── Detect external stress tools ──────────────────────────────────────────

    private void DetectTools()
    {
        var pf64 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        // Prime95 common install locations
        foreach (var path in new[]
        {
            Path.Combine(pf64, "Prime95", "prime95.exe"),
            Path.Combine(pf86, "Prime95", "prime95.exe"),
            @"C:\Prime95\prime95.exe",
        })
        {
            if (File.Exists(path)) { _prime95Path = path; IsPrime95Installed = true; break; }
        }

        // Cinebench R23 / 2024 common paths
        foreach (var path in new[]
        {
            Path.Combine(pf64, "Maxon Cinebench R23",   "Cinebench.exe"),
            Path.Combine(pf64, "Maxon Cinebench 2024",  "Cinebench.exe"),
            Path.Combine(pf86, "Maxon Cinebench R23",   "Cinebench.exe"),
            Path.Combine(pf86, "Maxon Cinebench 2024",  "Cinebench.exe"),
        })
        {
            if (File.Exists(path)) { _cinebenchPath = path; IsCinebenchInstalled = true; break; }
        }
    }

    // ── Built-in CPU stress test ──────────────────────────────────────────────

    public async Task RunCpuStressAsync(TimeSpan duration, int maxTempC, CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        _status.State            = StressTestState.Running;
        _status.MaxTempC         = 0;
        _status.CurrentTempC     = 0;
        _status.CurrentCpuLoad   = 0;
        _status.Elapsed          = TimeSpan.Zero;
        _status.AbortedByWatchdog = false;
        _status.Message          = $"Running stress test ({maxTempC}°C watchdog limit)...";
        StatusChanged?.Invoke();

        var sw          = Stopwatch.StartNew();
        var threadCount = Environment.ProcessorCount;
        var threads     = new Task[threadCount];

        // Spawn N CPU-bound worker threads doing tight SIMD-style math loops
        for (int i = 0; i < threadCount; i++)
        {
            threads[i] = Task.Run(() =>
            {
                var rng  = new Random();
                var data = Enumerable.Range(0, 1024).Select(_ => rng.NextDouble()).ToArray();
                while (!_cts.Token.IsCancellationRequested)
                {
                    double sum = 0;
                    for (int k = 0; k < data.Length; k++)
                        sum += Math.Sqrt(data[k] * data[k] + 1.0);
                    // prevent JIT from eliminating the loop
                    if (sum < 0) data[0] = sum;
                }
            }, _cts.Token);
        }

        // Monitor / watchdog loop — runs on a ThreadPool thread (not UI thread)
        try
        {
            while (sw.Elapsed < duration && !_cts.Token.IsCancellationRequested)
            {
                await Task.Delay(1000, _cts.Token);
                _status.Elapsed = sw.Elapsed;

                if (_sensors.IsAvailable)
                {
                    var snap = _sensors.GetSnapshot();
                    var temp = snap.CpuPackageTemperatureC ?? 0;
                    _status.CurrentTempC = temp;
                    if (temp > _status.MaxTempC) _status.MaxTempC = temp;

                    var load = snap.CpuLoads.FirstOrDefault()?.Value ?? 0;
                    _status.CurrentCpuLoad = load;

                    // Thermal watchdog
                    if (temp > maxTempC && temp > 0)
                    {
                        _status.AbortedByWatchdog = true;
                        _status.Message = $"WATCHDOG: aborted at {temp:F0}°C (limit {maxTempC}°C)";
                        StatusChanged?.Invoke();
                        _cts.Cancel();
                        break;
                    }
                }

                StatusChanged?.Invoke();
            }
        }
        catch (OperationCanceledException) { /* expected */ }

        // Stop all worker threads gracefully
        _cts.Cancel();
        try { await Task.WhenAll(threads).WaitAsync(TimeSpan.FromSeconds(5)); }
        catch { /* swallow TaskCanceledException / TimeoutException */ }

        sw.Stop();
        _status.Elapsed = sw.Elapsed;
        _status.State   = _status.AbortedByWatchdog ? StressTestState.Aborted : StressTestState.Completed;

        if (!_status.AbortedByWatchdog)
            _status.Message = $"Stress test completed in {sw.Elapsed.TotalSeconds:F0}s. Peak: {_status.MaxTempC:F0}°C.";

        StatusChanged?.Invoke();
    }

    public void Stop()
    {
        _cts?.Cancel();
    }

    // ── External tool launchers ───────────────────────────────────────────────

    public Task<bool> LaunchPrime95Async()
    {
        if (!IsPrime95Installed || _prime95Path == null) return Task.FromResult(false);
        try
        {
            Process.Start(new ProcessStartInfo(_prime95Path) { UseShellExecute = true });
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            EngineLog.Error("LaunchPrime95Async failed", ex);
            return Task.FromResult(false);
        }
    }

    public Task<bool> LaunchCinebenchAsync()
    {
        if (!IsCinebenchInstalled || _cinebenchPath == null) return Task.FromResult(false);
        try
        {
            Process.Start(new ProcessStartInfo(_cinebenchPath) { UseShellExecute = true });
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            EngineLog.Error("LaunchCinebenchAsync failed", ex);
            return Task.FromResult(false);
        }
    }
}
