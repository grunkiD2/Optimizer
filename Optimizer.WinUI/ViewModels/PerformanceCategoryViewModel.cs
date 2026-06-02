using CommunityToolkit.Mvvm.ComponentModel;
using Optimizer.WinUI.Helpers;
using Optimizer.WinUI.Services;
using Ids = Optimizer.WinUI.Models.OptimizationIds;

namespace Optimizer.WinUI.ViewModels;

public partial class PerformanceCategoryViewModel : CategoryViewModelBase
{
    private readonly SystemMonitorService _monitor;

    [ObservableProperty] private double cpuUsage;
    [ObservableProperty] private string cpuText = "0%";
    [ObservableProperty] private double memoryUsage;
    [ObservableProperty] private string memoryText = "0%";
    [ObservableProperty] private string usedMemoryText = "0 MB used";

    public override string CategoryName => "Performance";
    public override string CategoryIcon => "⚡";

    protected override string[] OptimizationIds =>
    [
        Ids.DisableBackgroundApps,
        Ids.DisableAnimations,
        Ids.DisableVisualEffects,
        Ids.OptimizePowerSettings,
        Ids.AdjustPageFileSize
    ];

    public PerformanceCategoryViewModel(
        IWindowsOptimizerService optimizer,
        IElevationService elevation,
        IUndoService undoSvc,
        HistoryService history,
        SystemMonitorService monitor)
        : base(optimizer, elevation, undoSvc, history)
    {
        _monitor = monitor;
    }

    public override void Load()
    {
        base.Load();
        RefreshMetrics();
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
}
