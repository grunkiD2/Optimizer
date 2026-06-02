using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;

namespace Optimizer.WinUI.ViewModels;

public partial class TuningViewModel : ObservableObject
{
    private readonly ITuningService    _tuning;
    private readonly IStressTestService _stress;
    private DispatcherQueue? _dispatcherQueue;
    private CancellationTokenSource? _stressCts;

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

    // ── Constructor ───────────────────────────────────────────────────────────

    public TuningViewModel(ITuningService tuning, IStressTestService stress)
    {
        _tuning  = tuning;
        _stress  = stress;

        // Pre-populate with all presets; LoadAsync will re-filter by vendor
        foreach (var p in tuning.GetPresets()) Presets.Add(p);

        _stress.StatusChanged += OnStressStatusChanged;
    }

    // Called from code-behind to supply the UI thread dispatcher
    public void InitDispatcher(DispatcherQueue queue)
    {
        _dispatcherQueue = queue;
    }

    // ── Load ──────────────────────────────────────────────────────────────────

    public async Task LoadAsync()
    {
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
}
