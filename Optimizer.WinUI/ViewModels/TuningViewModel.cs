using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Models.Gpu;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.Services.Gpu;

namespace Optimizer.WinUI.ViewModels;

public partial class TuningViewModel : ObservableObject
{
    private readonly ITuningService      _tuning;
    private readonly IStressTestService  _stress;
    private readonly IGpuControlService  _gpuControl;
    private DispatcherQueue? _dispatcherQueue;
    private CancellationTokenSource? _stressCts;
    private CancellationTokenSource? _gpuWatchdogCts;
    private System.Threading.Timer? _telemetryTimer;

    // ── Existing observable properties ────────────────────────────────────────

    [ObservableProperty] private CpuTuning currentCpu = new();
    [ObservableProperty] private RamInfo   ramInfo    = new();
    [ObservableProperty] private bool      isApplying;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string statusMessage = "";

    [ObservableProperty] private bool hasReadDisclaimer;

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

    public ObservableCollection<TuningPreset> Presets   { get; } = [];
    public ObservableCollection<GpuClockInfo> Gpus      { get; } = [];
    public ObservableCollection<VendorTool>   GpuTools  { get; } = [];

    public string CategoryName => "Tuning";
    public string CategoryIcon => "⚡";

    public string BoostModeDisplay => CurrentCpu.BoostMode switch
    {
        BoostMode.Disabled                        => "Disabled",
        BoostMode.Enabled                         => "Enabled",
        BoostMode.Aggressive                      => "Aggressive",
        BoostMode.EfficientEnabled                => "Efficient Enabled",
        BoostMode.EfficientAggressive             => "Efficient Aggressive",
        BoostMode.AggressiveAtGuaranteed          => "Aggressive at Guaranteed",
        BoostMode.EfficientAggressiveAtGuaranteed => "Efficient Aggressive at Guaranteed",
        _ => CurrentCpu.BoostMode.ToString()
    };

    // ── Batch 35: Vendor + stress test properties ─────────────────────────────

    [ObservableProperty] private string cpuVendor       = "";
    [ObservableProperty] private bool   isStressTesting;
    [ObservableProperty] private string stressStatus    = "Idle";
    [ObservableProperty] private double stressElapsedSec;
    [ObservableProperty] private double currentTempC;
    [ObservableProperty] private double maxTempC;
    [ObservableProperty] private double currentCpuLoad;
    [ObservableProperty] private int    watchdogTempLimit    = 90;
    [ObservableProperty] private int    stressDurationSeconds = 60;

    // Power limits (read-only display)
    [ObservableProperty] private string pl1Display = "N/A";
    [ObservableProperty] private string pl2Display = "N/A";

    // Formatted display helpers for temp/load (avoid converters in Run elements)
    public string CurrentTempDisplay  => CurrentTempC  > 0 ? $"{CurrentTempC:F0}°C"  : "—";
    public string MaxTempDisplay      => MaxTempC      > 0 ? $"{MaxTempC:F0}°C"      : "—";
    public string CurrentLoadDisplay  => CurrentCpuLoad > 0 ? $"{CurrentCpuLoad:F0}%" : "—";

    // External-tool availability
    public bool IsPrime95Installed   => _stress.IsPrime95Installed;
    public bool IsCinebenchInstalled => _stress.IsCinebenchInstalled;

    // CPU vendor tool availability (D3b — honest guidance, display only)
    public bool HasIntelXtu    => DetectXtuPath() != null;
    public bool HasRyzenMaster => DetectRyzenMasterPath() != null;

    private static string? DetectXtuPath() => DetectXtuPathPublic();
    private static string? DetectRyzenMasterPath() => DetectRyzenMasterPathPublic();

    public static string? DetectXtuPathPublic()
    {
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var path = Path.Combine(pf, "Intel", "Intel(R) Extreme Tuning Utility", "XTU.exe");
        return File.Exists(path) ? path : null;
    }

    public static string? DetectRyzenMasterPathPublic()
    {
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var path = Path.Combine(pf, "AMD", "RyzenMaster", "RyzenMaster.exe");
        return File.Exists(path) ? path : null;
    }

    // ── GPU OC observable properties ──────────────────────────────────────────

    [ObservableProperty] private bool   gpuOcWriteAvailable;
    [ObservableProperty] private string gpuOcUnavailableReason = "";
    [ObservableProperty] private bool   isGpuWatchdogRunning;
    [ObservableProperty] private string gpuOcStatus = "";

    // Live telemetry (bound in the GPU telemetry card)
    [ObservableProperty] private string gpuName          = "—";
    [ObservableProperty] private string gpuCoreClock     = "—";
    [ObservableProperty] private string gpuMemClock      = "—";
    [ObservableProperty] private string gpuTemp          = "—";
    [ObservableProperty] private string gpuPower         = "—";
    [ObservableProperty] private string gpuFan           = "—";
    [ObservableProperty] private string gpuLoad          = "—";

    // OC control values (from Capabilities ranges)
    [ObservableProperty] private int    gpuCoreOffsetMhz;
    [ObservableProperty] private int    gpuMemOffsetMhz;
    [ObservableProperty] private int    gpuPowerLimitPct  = 100;
    [ObservableProperty] private int    gpuTempLimitC     = 83;
    [ObservableProperty] private int    gpuWatchdogTempC  = 88;
    [ObservableProperty] private int    gpuWatchdogDurationSec = 30;
    [ObservableProperty] private bool   gpuFanManual;
    [ObservableProperty] private int    gpuFanPct         = 50;

    // Capabilities (for slider min/max in code-behind)
    public GpuControlCapabilities GpuCapabilities => _gpuControl.Capabilities;

    // ── Constructor ───────────────────────────────────────────────────────────

    public TuningViewModel(ITuningService tuning, IStressTestService stress, IGpuControlService gpuControl)
    {
        _tuning      = tuning;
        _stress      = stress;
        _gpuControl  = gpuControl;

        // Pre-populate with all presets; LoadAsync will re-filter by vendor
        foreach (var p in tuning.GetPresets()) Presets.Add(p);

        _stress.StatusChanged += OnStressStatusChanged;
    }

    // Called from code-behind to supply the UI thread dispatcher
    public void InitDispatcher(DispatcherQueue queue)
    {
        _dispatcherQueue = queue;

        // Start a 2-second telemetry refresh timer for the GPU section.
        // The timer fires on a ThreadPool thread; property updates are
        // marshalled to the UI thread via _dispatcherQueue.
        _telemetryTimer?.Dispose();
        _telemetryTimer = new System.Threading.Timer(
            _ => RefreshGpuTelemetry(),
            null,
            dueTime: TimeSpan.FromSeconds(1),
            period:  TimeSpan.FromSeconds(2));
    }

    // ── Load ──────────────────────────────────────────────────────────────────

    public async Task LoadAsync()
    {
        // GPU OC availability
        GpuOcWriteAvailable     = _gpuControl.OcWriteAvailable;
        GpuOcUnavailableReason  = _gpuControl.OcUnavailableReason ?? "GPU OC write not available.";
        OnPropertyChanged(nameof(GpuCapabilities));

        CurrentCpu = await _tuning.GetCurrentCpuTuningAsync();
        RamInfo    = await _tuning.GetRamInfoAsync();

        var gpus = await _tuning.GetGpuClocksAsync();
        Gpus.Clear();
        foreach (var g in gpus) Gpus.Add(g);

        var tools = await _tuning.DetectGpuToolsAsync();
        GpuTools.Clear();
        foreach (var t in tools) GpuTools.Add(t);

        OnPropertyChanged(nameof(BoostModeDisplay));

        // Detect vendor and filter presets
        var vendor = await _tuning.GetCpuVendorAsync();
        CpuVendor = vendor.Contains("Intel", StringComparison.OrdinalIgnoreCase) ? "Intel"
                  : vendor.Contains("AMD",   StringComparison.OrdinalIgnoreCase) ? "AMD"
                  : "Unknown";

        FilterPresetsByVendor(CpuVendor);

        // Power limits
        var (pl1, pl2) = await _tuning.GetPowerLimitsAsync();
        Pl1Display = pl1.HasValue ? $"{pl1} W" : "N/A";
        Pl2Display = pl2.HasValue ? $"{pl2} W" : "N/A";

        OnPropertyChanged(nameof(IsPrime95Installed));
        OnPropertyChanged(nameof(IsCinebenchInstalled));
    }

    private void FilterPresetsByVendor(string vendor)
    {
        var all = _tuning.GetPresets()
            .Where(p => p.CpuVendor == "Any" || p.CpuVendor.Equals(vendor, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Presets.Clear();
        foreach (var p in all) Presets.Add(p);
    }

    // ── Apply a preset card ───────────────────────────────────────────────────

    [RelayCommand]
    public async Task ApplyPresetAsync(TuningPreset preset)
    {
        if (!HasReadDisclaimer)
        {
            StatusMessage = "Please acknowledge the disclaimer before applying a preset.";
            return;
        }

        IsApplying = true;
        try
        {
            var ok = await _tuning.ApplyPresetAsync(preset);
            StatusMessage = ok
                ? $"Applied preset: {preset.Name}"
                : "Failed to apply preset — check app is running as Administrator.";

            if (ok) await LoadAsync();
        }
        finally { IsApplying = false; }
    }

    // ── Revert to stock defaults ──────────────────────────────────────────────

    [RelayCommand]
    public async Task RevertAsync()
    {
        IsApplying = true;
        try
        {
            var ok = await _tuning.RevertToDefaultsAsync();
            StatusMessage = ok
                ? "Reverted to Stock defaults."
                : "Revert failed — check app is running as Administrator.";
            if (ok) await LoadAsync();
        }
        finally { IsApplying = false; }
    }

    // ── Apply manually-configured CPU sliders ─────────────────────────────────

    [RelayCommand]
    public async Task ApplyCurrentCpuAsync()
    {
        if (!HasReadDisclaimer)
        {
            StatusMessage = "Please acknowledge the disclaimer first.";
            return;
        }

        IsApplying = true;
        try
        {
            var ok = await _tuning.ApplyCpuTuningAsync(CurrentCpu);
            StatusMessage = ok
                ? "CPU tuning applied."
                : "Failed to apply CPU tuning — check app is running as Administrator.";
            if (ok) await LoadAsync();
        }
        finally { IsApplying = false; }
    }

    // ── Launch a vendor GPU tool ──────────────────────────────────────────────

    [RelayCommand]
    public async Task LaunchToolAsync(VendorTool tool)
    {
        await _tuning.LaunchToolAsync(tool);
    }

    // ── Batch 35: Stress test commands ───────────────────────────────────────

    [RelayCommand]
    public async Task RunBuiltInStressAsync()
    {
        if (!HasReadDisclaimer)
        {
            StatusMessage = "Please acknowledge the disclaimer before running a stress test.";
            return;
        }

        if (IsStressTesting) return;

        IsStressTesting = true;
        _stressCts      = new CancellationTokenSource();
        try
        {
            await _stress.RunCpuStressAsync(
                TimeSpan.FromSeconds(StressDurationSeconds),
                WatchdogTempLimit,
                _stressCts.Token);
        }
        catch (OperationCanceledException) { /* user stopped */ }
        finally
        {
            IsStressTesting = false;
        }
    }

    [RelayCommand]
    public void StopStress()
    {
        _stress.Stop();
        _stressCts?.Cancel();
    }

    [RelayCommand]
    public async Task LaunchPrime95Async()
    {
        var ok = await _stress.LaunchPrime95Async();
        if (!ok)
            StatusMessage = "Prime95 not found. Install it manually then retry.";
    }

    [RelayCommand]
    public async Task LaunchCinebenchAsync()
    {
        var ok = await _stress.LaunchCinebenchAsync();
        if (!ok)
            StatusMessage = "Cinebench not found. Install Cinebench R23 or 2024 then retry.";
    }

    // ── Stress status callback (background thread → UI thread) ───────────────

    private void OnStressStatusChanged()
    {
        void Update()
        {
            var s = _stress.Status;
            StressStatus     = s.Message;
            StressElapsedSec = s.Elapsed.TotalSeconds;
            CurrentTempC     = s.CurrentTempC;
            MaxTempC         = s.MaxTempC;
            CurrentCpuLoad   = s.CurrentCpuLoad;
            OnPropertyChanged(nameof(CurrentTempDisplay));
            OnPropertyChanged(nameof(MaxTempDisplay));
            OnPropertyChanged(nameof(CurrentLoadDisplay));

            if (s.State != StressTestState.Running)
            {
                IsStressTesting = false;

                // Thermal watchdog triggered — auto-revert to Stock preset
                if (s.AbortedByWatchdog)
                {
                    StatusMessage = $"Thermal watchdog fired at {s.MaxTempC:F0}°C — auto-reverting to Stock preset.";
                    // Fire-and-forget revert; ignore result (best effort)
                    _ = _tuning.RevertToDefaultsAsync().ContinueWith(t =>
                    {
                        if (t.IsCompletedSuccessfully && t.Result)
                            EngineLog.Write("Thermal watchdog: reverted to Stock preset.");
                    }, TaskScheduler.Default);
                }
            }
        }

        if (_dispatcherQueue != null)
            _dispatcherQueue.TryEnqueue(Update);
        else
            Update(); // No dispatcher set yet — call directly (should not happen in normal flow)
    }

    // ── GPU OC commands ───────────────────────────────────────────────────────

    [RelayCommand]
    public void ApplyGpuOc()
    {
        if (!HasReadDisclaimer)
        {
            GpuOcStatus = "Please acknowledge the disclaimer before applying GPU overclocking.";
            return;
        }

        var desired = BuildDesiredState();
        var (ok, error, applied) = _gpuControl.Apply(desired);

        GpuOcStatus = ok
            ? $"GPU settings applied: core +{applied.CoreClockOffsetMhz} MHz, " +
              $"mem +{applied.MemoryClockOffsetMhz} MHz, " +
              $"power {applied.PowerLimitPercent}%, " +
              $"temp limit {applied.TempLimitC}°C."
            : $"GPU apply failed: {error}";
    }

    [RelayCommand]
    public async Task ApplyGpuOcWithWatchdogAsync()
    {
        if (!HasReadDisclaimer)
        {
            GpuOcStatus = "Please acknowledge the disclaimer before applying GPU overclocking.";
            return;
        }

        if (IsGpuWatchdogRunning) return;

        IsGpuWatchdogRunning = true;
        _gpuWatchdogCts      = new CancellationTokenSource();
        GpuOcStatus          = "GPU watchdog test running...";

        try
        {
            var desired = BuildDesiredState();
            var result = await _gpuControl.ApplyWithWatchdogAsync(
                desired,
                GpuWatchdogTempC,
                TimeSpan.FromSeconds(GpuWatchdogDurationSec),
                _gpuWatchdogCts.Token);

            GpuOcStatus = result;
        }
        catch (OperationCanceledException)
        {
            GpuOcStatus = "GPU watchdog test cancelled.";
        }
        finally
        {
            IsGpuWatchdogRunning = false;
        }
    }

    [RelayCommand]
    public void StopGpuWatchdog()
    {
        _gpuWatchdogCts?.Cancel();
    }

    [RelayCommand]
    public void ResetGpuToDefault()
    {
        _gpuControl.ResetToDefault();
        GpuCoreOffsetMhz  = 0;
        GpuMemOffsetMhz   = 0;
        GpuPowerLimitPct  = 100;
        GpuTempLimitC     = 83;
        GpuFanManual      = false;
        // Audit C3: the backends' ResetToDefault is a documented no-op when OC write is
        // unavailable — reporting "reset to defaults" then was a false success message.
        GpuOcStatus = _gpuControl.OcWriteAvailable
            ? "GPU settings reset to defaults."
            : "Sliders reset. Nothing was written — OC write is not available in this build.";
    }

    [RelayCommand]
    public async Task OpenGpuVendorToolAsync()
    {
        var tools = await _tuning.DetectGpuToolsAsync();
        var best  = tools.FirstOrDefault(t => t.IsInstalled) ?? tools.FirstOrDefault();
        if (best != null)
            await _tuning.LaunchToolAsync(best);
    }

    // ── GPU telemetry refresh (called by timer) ───────────────────────────────

    private void RefreshGpuTelemetry()
    {
        try
        {
            var snapshots = _gpuControl.ReadTelemetry();
            var snap      = snapshots.FirstOrDefault();

            void Update()
            {
                if (snap == null)
                {
                    GpuName      = "No GPU data";
                    GpuCoreClock = "—";
                    GpuMemClock  = "—";
                    GpuTemp      = "—";
                    GpuPower     = "—";
                    GpuFan       = "—";
                    GpuLoad      = "—";
                }
                else
                {
                    GpuName      = snap.Name;
                    GpuCoreClock = snap.CoreClockMhz.HasValue  ? $"{snap.CoreClockMhz:F0} MHz"  : "—";
                    GpuMemClock  = snap.MemoryClockMhz.HasValue ? $"{snap.MemoryClockMhz:F0} MHz" : "—";
                    GpuTemp      = snap.TemperatureC.HasValue   ? $"{snap.TemperatureC:F0}°C"     : "—";
                    GpuPower     = snap.PowerWatts.HasValue     ? $"{snap.PowerWatts:F0} W"       : "—";
                    GpuFan       = snap.FanRpm.HasValue         ? $"{snap.FanRpm:F0} RPM"         : "—";
                    GpuLoad      = snap.LoadPercent.HasValue    ? $"{snap.LoadPercent:F0}%"        : "—";
                }
            }

            if (_dispatcherQueue != null)
                _dispatcherQueue.TryEnqueue(Update);
        }
        catch { /* non-fatal: telemetry refresh errors should not crash the VM */ }
    }

    // ── Helper: build GpuControlState from current slider values ─────────────

    private GpuControlState BuildDesiredState() => new()
    {
        CoreClockOffsetMhz   = GpuCoreOffsetMhz,
        MemoryClockOffsetMhz = GpuMemOffsetMhz,
        PowerLimitPercent    = GpuPowerLimitPct,
        TempLimitC           = GpuTempLimitC,
        FanPercent           = GpuFanManual ? GpuFanPct : (int?)null,
    };
}
