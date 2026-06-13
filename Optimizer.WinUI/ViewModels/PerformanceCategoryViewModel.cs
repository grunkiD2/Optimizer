using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Optimizer.WinUI.Helpers;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;
using Ids = Optimizer.WinUI.Models.OptimizationIds;

namespace Optimizer.WinUI.ViewModels;

public partial class PerformanceCategoryViewModel : CategoryViewModelBase
{
    private readonly ISystemMonitorService _monitor;
    private readonly IPowerService _powerService;
    private readonly IProcessService _processService;

    // ── Live metrics ─────────────────────────────────────────────────────────
    [ObservableProperty] private double cpuUsage;
    [ObservableProperty] private string cpuText = "0%";
    [ObservableProperty] private double memoryUsage;
    [ObservableProperty] private string memoryText = "0%";
    [ObservableProperty] private string usedMemoryText = "0 MB used";

    // ── Power plans ──────────────────────────────────────────────────────────
    public ObservableCollection<PowerPlan> PowerPlans { get; } = [];
    [ObservableProperty] private PowerPlan? activePowerPlan;
    [ObservableProperty] private bool gameModeEnabled;

    // ── Process manager ──────────────────────────────────────────────────────
    public ObservableCollection<ProcessPriorityInfo> Processes { get; } = [];

    // ── Affinity editor ──────────────────────────────────────────────────────
    /// <summary>Number of logical cores; drives the per-core checkbox list.</summary>
    public int LogicalCoreCount => _processService.LogicalCoreCount;

    public override string CategoryName => "Performance";
    public override string CategoryIcon => "⚡";

    protected override string[] OptimizationIds =>
    [
        Ids.DisableBackgroundApps,
        Ids.DisableAnimations,
        Ids.DisableVisualEffects,
        Ids.OptimizePowerSettings,
        Ids.AdjustPageFileSize,
        // Audit Batch 2: these were registered + fully implemented but appeared on no page.
        Ids.DisableTransparencyEffects,
        Ids.EnableAccentTitleBars
    ];

    public PerformanceCategoryViewModel(
        IWindowsOptimizerService optimizer,
        IElevationService elevation,
        IUndoService undoSvc,
        IHistoryService history,
        ISystemMonitorService monitor,
        IPowerService powerService,
        IProcessService processService)
        : base(optimizer, elevation, undoSvc, history)
    {
        _monitor = monitor;
        _powerService = powerService;
        _processService = processService;
    }

    public override void Load()
    {
        base.Load();
        RefreshMetrics();
    }

    public async Task LoadPowerAsync()
    {
        var plans = await _powerService.GetPowerPlansAsync();
        PowerPlans.Clear();
        foreach (var p in plans) PowerPlans.Add(p);
        ActivePowerPlan = plans.FirstOrDefault(p => p.IsActive);
        GameModeEnabled = _powerService.IsGameModeEnabled();

        Processes.Clear();
        foreach (var p in _processService.GetUserProcesses()) Processes.Add(p);
    }

    public void RefreshMetrics()
    {
        var snapshot = _monitor.CollectSnapshot();

        CpuUsage = snapshot.CpuUsagePercentage;
        CpuText = $"{snapshot.CpuUsagePercentage:F0}%";

        var totalMem = snapshot.TotalPhysicalMemory;
        var availMem = snapshot.AvailablePhysicalMemory;
        var usedBytes = totalMem - availMem;

        MemoryUsage = totalMem > 0 ? 100.0 * usedBytes / totalMem : 0;
        MemoryText = $"{MemoryUsage:F0}%";

        UsedMemoryText = $"{ByteFormatter.Format(usedBytes)} used";
    }

    [RelayCommand]
    public async Task SetPowerPlanAsync(PowerPlan plan)
    {
        if (await _powerService.SetActivePowerPlanAsync(plan.Guid))
            await LoadPowerAsync();
    }

    [RelayCommand]
    public async Task CreateUltimatePlanAsync()
    {
        if (await _powerService.CreateUltimatePerformancePlanAsync())
            await LoadPowerAsync();
    }

    partial void OnGameModeEnabledChanged(bool value)
    {
        _ = _powerService.SetGameModeAsync(value);
    }

    // ── Priority ─────────────────────────────────────────────────────────────

    public bool SetProcessPriority(int pid, ProcessPriorityClass priority)
        => _processService.SetProcessPriority(pid, priority);

    // ── Affinity (per-core) ───────────────────────────────────────────────────

    /// <summary>
    /// Returns the current affinity bitmask for a process,
    /// falling back to all-cores if the call fails.
    /// </summary>
    public long GetAffinity(int pid)
        => _processService.GetProcessAffinityMask(pid)
           ?? AffinityMask.AllCores(LogicalCoreCount);

    /// <summary>Sets an explicit per-core affinity mask. Returns false on failure.</summary>
    public bool SetAffinity(int pid, long mask)
        => _processService.SetProcessAffinityMask(pid, mask);
}
