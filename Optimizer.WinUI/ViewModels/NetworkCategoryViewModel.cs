using CommunityToolkit.Mvvm.ComponentModel;
using Optimizer.WinUI.Helpers;
using Optimizer.WinUI.Services;
using Ids = Optimizer.WinUI.Models.OptimizationIds;

namespace Optimizer.WinUI.ViewModels;

public partial class NetworkCategoryViewModel : CategoryViewModelBase
{
    private readonly SystemMonitorService _monitor;

    [ObservableProperty] private string downloadSpeedText = "0 B/s";
    [ObservableProperty] private string uploadSpeedText = "0 B/s";
    [ObservableProperty] private string latencyText = "N/A";

    public override string CategoryName => "Network";
    public override string CategoryIcon => "🌐";

    protected override string[] OptimizationIds =>
    [
        Ids.OptimizeNetworkSettings,
        Ids.FlushDnsCache
    ];

    public NetworkCategoryViewModel(
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

        DownloadSpeedText = ByteFormatter.FormatSpeed(snapshot.NetworkInSpeed);
        UploadSpeedText = ByteFormatter.FormatSpeed(snapshot.NetworkOutSpeed);
        LatencyText = "N/A";
    }
}
