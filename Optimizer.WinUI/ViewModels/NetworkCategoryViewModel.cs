using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Optimizer.WinUI.Helpers;
using Optimizer.WinUI.Services;
using Ids = Optimizer.WinUI.Models.OptimizationIds;

namespace Optimizer.WinUI.ViewModels;

public partial class NetworkCategoryViewModel : CategoryViewModelBase
{
    private readonly SystemMonitorService _monitor;
    private readonly INetworkConfigService _netConfig;

    [ObservableProperty] private string downloadSpeedText = "0 B/s";
    [ObservableProperty] private string uploadSpeedText = "0 B/s";
    [ObservableProperty] private string latencyText = "N/A";

    [ObservableProperty] private string currentDns = "Loading...";
    [ObservableProperty] private DnsServerPreset? selectedDnsPreset;

    public IReadOnlyList<DnsServerPreset> DnsPresets => _netConfig.DnsPresets;

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
        SystemMonitorService monitor,
        INetworkConfigService netConfig)
        : base(optimizer, elevation, undoSvc, history)
    {
        _monitor = monitor;
        _netConfig = netConfig;
    }

    public override void Load()
    {
        base.Load();
        RefreshMetrics();
    }

    public async Task LoadDnsAsync()
    {
        var dns = await _netConfig.GetCurrentPrimaryDnsAsync();
        CurrentDns = string.IsNullOrWhiteSpace(dns) ? "Automatic (ISP)" : dns;
    }

    public void RefreshMetrics()
    {
        var snapshot = _monitor.CollectSnapshot();

        DownloadSpeedText = ByteFormatter.FormatSpeed(snapshot.NetworkInSpeed);
        UploadSpeedText = ByteFormatter.FormatSpeed(snapshot.NetworkOutSpeed);
        LatencyText = "N/A";
    }

    [RelayCommand]
    public async Task ApplyDnsPresetAsync(DnsServerPreset preset)
    {
        var ok = await _netConfig.SetDnsAsync(preset.Primary, preset.Secondary);
        if (ok)
            CurrentDns = preset.Primary;
    }

    [RelayCommand]
    public async Task ResetDnsAsync()
    {
        if (await _netConfig.ResetDnsToAutomaticAsync())
            CurrentDns = "Automatic (ISP)";
    }

    [RelayCommand]
    public async Task FlushDnsCacheAsync()
    {
        await _netConfig.FlushDnsAsync();
    }
}
