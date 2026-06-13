using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Optimizer.WinUI.Helpers;
using Optimizer.WinUI.Services;
using Ids = Optimizer.WinUI.Models.OptimizationIds;

namespace Optimizer.WinUI.ViewModels;

public partial class NetworkCategoryViewModel : CategoryViewModelBase
{
    private readonly ISystemMonitorService _monitor;
    private readonly INetworkConfigService _netConfig;
    private readonly INetworkSpeedTestService _speedTest;
    private readonly ISystemDataBus _dataBus;

    // ── Live traffic metrics (existing) ──────────────────────────────────────
    [ObservableProperty] private string downloadSpeedText = "0 B/s";
    [ObservableProperty] private string uploadSpeedText = "0 B/s";

    // ── DNS ───────────────────────────────────────────────────────────────────
    [ObservableProperty] private string currentDns = "Loading...";
    [ObservableProperty] private DnsServerPreset? selectedDnsPreset;

    // ── Speed test ────────────────────────────────────────────────────────────
    [ObservableProperty] private SpeedTestResult? lastSpeedTest;
    [ObservableProperty] private bool isTestingSpeed;
    [ObservableProperty] private string speedTestPhase = "Press Run Test to measure your connection";

    // Formatted result strings for XAML Run bindings (x:Bind on Run.Text doesn't support converters)
    [ObservableProperty] private string downloadResultText = "—";
    [ObservableProperty] private string uploadResultText   = "—";
    [ObservableProperty] private string pingResultText     = "—";
    [ObservableProperty] private string jitterResultText   = "—";

    // ── Latency monitor ───────────────────────────────────────────────────────
    [ObservableProperty] private double currentPingMs;
    [ObservableProperty] private string currentPingText = "—";
    public ObservableCollection<double> LatencyHistory { get; } = [];

    private DispatcherQueue? _dispatcherQueue;
    private bool _latencyActive;

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
        IHistoryService history,
        ISystemMonitorService monitor,
        INetworkConfigService netConfig,
        INetworkSpeedTestService speedTest,
        ISystemDataBus dataBus)
        : base(optimizer, elevation, undoSvc, history)
    {
        _monitor   = monitor;
        _netConfig = netConfig;
        _speedTest = speedTest;
        _dataBus   = dataBus;
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
        // Use cached metrics if available, otherwise fall back to a fresh snapshot
        var snapshot = _dataBus.LatestMetrics ?? _monitor.CollectSnapshot();
        DownloadSpeedText = ByteFormatter.FormatSpeed(snapshot.NetworkInSpeed);
        UploadSpeedText   = ByteFormatter.FormatSpeed(snapshot.NetworkOutSpeed);
    }

    // ── Speed test ─────────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task RunSpeedTestAsync()
    {
        if (IsTestingSpeed) return;
        IsTestingSpeed = true;
        LastSpeedTest  = null;
        try
        {
            var progress = new Progress<string>(phase => SpeedTestPhase = phase);
            var result   = await _speedTest.RunFullTestAsync(progress);
            LastSpeedTest      = result;
            DownloadResultText = result.DownloadMbps.ToString("F1");
            UploadResultText   = result.UploadMbps.ToString("F1");
            PingResultText     = result.PingMs.ToString("F1");
            JitterResultText   = result.JitterMs.ToString("F1");
            SpeedTestPhase     = $"Completed at {result.TestedAt:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            SpeedTestPhase = $"Test failed: {ex.Message}";
        }
        finally
        {
            IsTestingSpeed = false;
        }
    }

    // ── Latency monitor ────────────────────────────────────────────────────────

    /// <summary>Call from Page.Loaded to start continuous 1 Hz ping via the data bus.</summary>
    public void StartLatencyMonitor(DispatcherQueue dispatcherQueue)
    {
        _dispatcherQueue = dispatcherQueue;
        if (_latencyActive) return;
        _latencyActive = true;
        _dataBus.LatencyUpdated += OnLatencyUpdated;
        _dataBus.MetricsUpdated += OnMetricsUpdated; // live download/upload (Batch 4a: were frozen snapshots)
        _dataBus.SetLatencyActive(true);
    }

    /// <summary>Call from Page.Unloaded to stop continuous ping.</summary>
    public void StopLatencyMonitor()
    {
        if (!_latencyActive) return;
        _latencyActive = false;
        _dataBus.LatencyUpdated -= OnLatencyUpdated;
        _dataBus.MetricsUpdated -= OnMetricsUpdated;
        _dataBus.SetLatencyActive(false);
    }

    private void OnMetricsUpdated(Optimizer.WinUI.Models.SystemResource m)
    {
        _dispatcherQueue?.TryEnqueue(() =>
        {
            DownloadSpeedText = ByteFormatter.FormatSpeed(m.NetworkInSpeed);
            UploadSpeedText   = ByteFormatter.FormatSpeed(m.NetworkOutSpeed);
        });
    }

    private void OnLatencyUpdated(double ms)
    {
        _dispatcherQueue?.TryEnqueue(() =>
        {
            CurrentPingMs   = ms;
            CurrentPingText = ms > 0 ? ms.ToString("F1") : "—";

            // Keep 60-entry ring buffer — normalised to 0–100 for sparkline
            // Map 0–300 ms linearly to 0–100
            double normalized = Math.Clamp(ms / 3.0, 0, 100);
            if (LatencyHistory.Count >= 60)
                LatencyHistory.RemoveAt(0);
            LatencyHistory.Add(normalized);
        });
    }

    // ── DNS ────────────────────────────────────────────────────────────────────

    // Audit Batch 2: these used to discard the bool result — failures (usually missing
    // elevation) and even successes gave no feedback. Now they surface via the status InfoBar.
    [RelayCommand]
    public async Task ApplyDnsPresetAsync(DnsServerPreset preset)
    {
        var ok = await _netConfig.SetDnsAsync(preset.Primary, preset.Secondary);
        if (ok) { CurrentDns = preset.Primary; SetStatus($"DNS set to {preset.Name} ({preset.Primary}).", false); }
        else SetStatus($"Couldn't set DNS to {preset.Name} — requires administrator.", true);
    }

    [RelayCommand]
    public async Task ResetDnsAsync()
    {
        if (await _netConfig.ResetDnsToAutomaticAsync()) { CurrentDns = "Automatic (ISP)"; SetStatus("DNS reset to automatic (ISP).", false); }
        else SetStatus("Couldn't reset DNS — requires administrator.", true);
    }

    [RelayCommand]
    public async Task FlushDnsCacheAsync()
    {
        await _netConfig.FlushDnsAsync();
        SetStatus("DNS resolver cache flushed.", false);
    }
}
