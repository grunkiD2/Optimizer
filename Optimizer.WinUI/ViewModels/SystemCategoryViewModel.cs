using CommunityToolkit.Mvvm.ComponentModel;
using Optimizer.WinUI.Services;

namespace Optimizer.WinUI.ViewModels;

public partial class SystemCategoryViewModel : CategoryViewModelBase
{
    [ObservableProperty] private string telemetryStatus = "Unknown";

    public override string CategoryName => "System";
    public override string CategoryIcon => "🖥️";

    protected override string[] OptimizationIds =>
    [
        "DisableTelemetry",
        "DisableConsumerFeatures",
        "DisableHibernation"
    ];

    public SystemCategoryViewModel(
        IWindowsOptimizerService optimizer,
        IElevationService elevation,
        IUndoService undoSvc,
        HistoryService history)
        : base(optimizer, elevation, undoSvc, history)
    {
    }

    public override void Load()
    {
        base.Load();
        RefreshMetrics();
    }

    public void RefreshMetrics()
    {
        TelemetryStatus = Optimizer.IsOptimizationApplied("DisableTelemetry") == true
            ? "Disabled"
            : "Active";
    }
}
