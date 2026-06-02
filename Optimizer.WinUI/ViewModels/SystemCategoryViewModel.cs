using CommunityToolkit.Mvvm.ComponentModel;
using Optimizer.WinUI.Services;
using Ids = Optimizer.WinUI.Models.OptimizationIds;

namespace Optimizer.WinUI.ViewModels;

public partial class SystemCategoryViewModel : CategoryViewModelBase
{
    [ObservableProperty] private string telemetryStatus = "Unknown";

    public override string CategoryName => "System";
    public override string CategoryIcon => "🖥️";

    protected override string[] OptimizationIds =>
    [
        Ids.DisableTelemetry,
        Ids.DisableConsumerFeatures,
        Ids.DisableHibernation
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
        TelemetryStatus = Optimizer.IsOptimizationApplied(Ids.DisableTelemetry) == true
            ? "Disabled"
            : "Active";
    }
}
