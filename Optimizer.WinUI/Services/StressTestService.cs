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
        // Fail-safe (audit 4a-3): the thermal watchdog can only protect the CPU while temperatures
        // are readable. If LHM is offline at start we'd run a full-load burn with NO temperature
        // visibility and no way to abort on overheat — yet still show "(NN°C watchdog limit)".
        // Refuse to start instead of pretending the limit applies.
        if (!_sensors.IsAvailable)
        {
            _status.State             = StressTestState.Aborted;
            _status.AbortedByWatchdog = false;
            _status.MaxTempC          = 0;
            _status.CurrentTempC      = 0;
            _status.CurrentCpuLoad    = 0;
            _status.Elapsed           = TimeSpan.Zero;
            _status.Message           = "Cannot start: thermal sensors are unavailable (LHM offline). "
                                      + "Refusing to run a stress test with no watchdog protection.";
            StatusChanged?.Invoke();
            return;
        }

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

        // Monitor / watchdog loop — runs on a ThreadPool thread (not UI thread).
        // The blind/frozen windows below are measured in WALL-CLOCK time, not loop iterations:
        // the external-LHM backend's GetSnapshot() does a blocking HTTP GET (up to a 2 s timeout),
        // so an iteration is NOT reliably 1 s. Counting iterations would let an unprotected burn
        // run far longer than intended (audit 4a-3 review finding).
        const double maxBlindSeconds  = 3;   // no valid temp for this long (wall-clock) → abort
        const double maxFrozenSeconds = 8;   // liveness signal unchanged this long → stuck sensor
        var    lastValidAt    = TimeSpan.Zero;   // when we last had a usable temperature
        var    lastLivenessAt = TimeSpan.Zero;   // when the liveness signal (CPU power) last moved
        double lastLiveness   = double.NaN;
        bool   abortedForSafety = false;         // aborted for safety (telemetry), not a thermal trip
        try
        {
            while (sw.Elapsed < duration && !_cts.Token.IsCancellationRequested)
            {
                await Task.Delay(1000, _cts.Token);
                var now = sw.Elapsed;
                _status.Elapsed = now;

                // Always probe — don't gate on IsAvailable. It latches false on the first failed
                // GET and only recovers INSIDE GetSnapshot, so probing every iteration is what lets
                // a recovered server resume monitoring. GetSnapshot swallows its own errors and
                // returns an empty snapshot, so this never throws into the loop.
                var snap = _sensors.GetSnapshot();
                double? temp = snap.CpuPackageTemperatureC;
                _status.CurrentCpuLoad = snap.CpuLoads.FirstOrDefault()?.Value ?? 0;

                if (temp is double t && t > 0)
                {
                    lastValidAt = now;
                    _status.CurrentTempC = t;
                    if (t > _status.MaxTempC) _status.MaxTempC = t;

                    // Thermal watchdog — at-limit counts as over-limit for a safety device.
                    if (t >= maxTempC)
                    {
                        _status.AbortedByWatchdog = true;
                        _status.Message = $"WATCHDOG: aborted at {t:F0}°C (limit {maxTempC}°C)";
                        StatusChanged?.Invoke();
                        _cts.Cancel();
                        break;
                    }

                    // Frozen-sensor fail-safe (audit 4a-3 review): the Intel package TEMP comes from
                    // the integer DTS and can legitimately plateau at a whole degree for seconds, so
                    // it can't tell a stuck sensor from a steady burn. CPU package POWER (0.1 W
                    // resolution) is a better liveness signal: LHM republishes data.json only ~every
                    // 2 s while we poll every 1 s, so up to ~2 s of identical readings is NORMAL — but
                    // a value frozen for the full 8 s window (~4 publishes) means the driver has hung
                    // and is re-serving stale data, so abort. Fall back to any CPU power leaf if the
                    // "Package" sensor is ever renamed (mirrors CpuPackageTemperatureC), so a renamed
                    // sensor can't silently turn the detector into a no-op.
                    var liveness = snap.CpuPowerWatts ?? snap.CpuPowers.FirstOrDefault()?.Value;
                    if (liveness is double w)
                    {
                        if (w == lastLiveness)
                        {
                            if ((now - lastLivenessAt).TotalSeconds >= maxFrozenSeconds)
                            {
                                abortedForSafety = true;
                                _status.Message  = $"Thermal telemetry frozen ({t:F0}°C, {w:F0} W stuck) — stress test aborted for safety.";
                                StatusChanged?.Invoke();
                                _cts.Cancel();
                                break;
                            }
                        }
                        else { lastLiveness = w; lastLivenessAt = now; }
                    }
                }
                else
                {
                    // No usable reading. Clear the displayed temp so the UI shows "—" instead of a
                    // stale value that implies live monitoring, then abort once we've been blind for
                    // the wall-clock window.
                    _status.CurrentTempC = 0;
                    if ((now - lastValidAt).TotalSeconds >= maxBlindSeconds)
                    {
                        abortedForSafety = true;
                        _status.Message  = "Thermal telemetry lost (LHM offline) — stress test aborted for safety.";
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
        try { await Task.WhenAll(threads).WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None); }
        catch { /* swallow TaskCanceledException / TimeoutException */ }

        sw.Stop();
        _status.Elapsed = sw.Elapsed;
        _status.State   = (_status.AbortedByWatchdog || abortedForSafety)
            ? StressTestState.Aborted
            : StressTestState.Completed;

        if (_status.State == StressTestState.Completed)
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
