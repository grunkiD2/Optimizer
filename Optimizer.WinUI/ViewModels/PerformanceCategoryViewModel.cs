using CommunityToolkit.Mvvm.ComponentModel;
using Optimizer.WinUI.Services;

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
        "DisableBackgroundApps",
        "DisableAnimations",
        "DisableVisualEffects",
        "OptimizePowerSettings",
        "AdjustPageFileSize"
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

        // Format used memory as MB or GB
        if (usedBytes >= 1_073_741_824)
            UsedMemoryText = $"{usedBytes / 1_073_741_824.0:F1} GB used";
        else
            UsedMemoryText = $"{usedBytes / 1_048_576} MB used";
    }
}
