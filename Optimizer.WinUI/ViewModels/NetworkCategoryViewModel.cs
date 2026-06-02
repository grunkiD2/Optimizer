using CommunityToolkit.Mvvm.ComponentModel;
using Optimizer.WinUI.Services;

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
        "OptimizeNetworkSettings",
        "FlushDnsCache"
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

        DownloadSpeedText = FormatSpeed(snapshot.NetworkInSpeed);
        UploadSpeedText = FormatSpeed(snapshot.NetworkOutSpeed);
        LatencyText = "N/A";
    }

    private static string FormatSpeed(double bytesPerSec)
    {
        if (bytesPerSec >= 1_048_576)
            return $"{bytesPerSec / 1_048_576:F1} MB/s";
        if (bytesPerSec >= 1_024)
            return $"{bytesPerSec / 1_024:F0} KB/s";
        return $"{bytesPerSec:F0} B/s";
    }
}
