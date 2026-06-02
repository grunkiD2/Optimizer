using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Optimizer.WinUI.Helpers;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;

namespace Optimizer.WinUI.ViewModels;

public partial class DashboardViewModel : ObservableObject, IDisposable
{
    private readonly SystemMonitorService _monitor;
    private readonly IWindowsOptimizerService _optimizer;
    private readonly IProcessService _processService;
    private readonly IUndoService _undoService;
    private readonly SettingsService _settings;
    private readonly ISensorService _sensorService;
    private readonly IIntelligenceService _intelligence;

    private DispatcherTimer? _timer;
    private bool _disposed;
    private int _anomalyCheckCounter;
    private DispatcherQueue? _dispatcherQueue;
    private volatile bool _initialRefreshDone;
    private volatile bool _refreshInFlight;

    // ── Metric properties ────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CpuText))]
    private double _cpuUsage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MemoryText))]
    private double _memoryUsage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GpuText))]
    private double _gpuUsage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiskText))]
    private double _diskUsage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NetworkText))]
    private double _networkUsage;

    [ObservableProperty] private int _totalCores;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalMemoryText))]
    private long _totalMemoryBytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsedMemoryText))]
    private long _usedMemoryBytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiskReadText))]
    private double _diskReadSpeed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiskWriteText))]
    private double _diskWriteSpeed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NetworkInText))]
    private double _networkInSpeed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NetworkOutText))]
    private double _networkOutSpeed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HealthScoreText))]
    private int _healthScore = 100;

    [ObservableProperty] private string _healthText = "System is healthy";

    [ObservableProperty] private int _undoableChanges;

    [ObservableProperty] private string _lastUpdated = "--:--:--";
    [ObservableProperty] private bool _isBusy;

    // ── Sensor properties (from LibreHardwareMonitor) ─────────────────────────
    [ObservableProperty] private string _cpuTempText = "—";
    [ObservableProperty] private string _gpuTempText = "—";
    [ObservableProperty] private string _cpuPowerText = "—";
    [ObservableProperty] private string _gpuPowerText = "—";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string _statusMessage = string.Empty;

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

    // ── Formatted display strings (used by x:Bind in XAML) ─────────────────

    public string CpuText => $"{CpuUsage:F1}%";
    public string MemoryText => $"{MemoryUsage:F1}%";
    public string GpuText => $"{GpuUsage:F1}%";
    public string DiskText => $"{DiskUsage:F1}%";
    public string NetworkText => $"{NetworkUsage:F1}%";
    public string UsedMemoryText => ByteFormatter.Format(UsedMemoryBytes);
    public string TotalMemoryText => ByteFormatter.Format(TotalMemoryBytes);
    public string DiskReadText => ByteFormatter.FormatSpeed(DiskReadSpeed);
    public string DiskWriteText => ByteFormatter.FormatSpeed(DiskWriteSpeed);
    public string NetworkInText => ByteFormatter.FormatSpeed(NetworkInSpeed);
    public string NetworkOutText => ByteFormatter.FormatSpeed(NetworkOutSpeed);
    public string HealthScoreText => $"{HealthScore}/100";

    // ── Collections ─────────────────────────────────────────────────────────

    public ObservableCollection<ProcessInfo> TopProcesses { get; } = new();
    public ObservableCollection<double> PerCoreUsage { get; } = new();
    public ObservableCollection<SystemResource> ChartHistory { get; } = new();

    // ── Sparkline history (one double per metric tick) ───────────────────────
    public ObservableCollection<double> CpuHistory { get; } = new();
    public ObservableCollection<double> MemoryHistory { get; } = new();
    public ObservableCollection<double> GpuHistory { get; } = new();
    public ObservableCollection<double> DiskHistory { get; } = new();
    public ObservableCollection<double> NetworkHistory { get; } = new();

    // ── Commands ─────────────────────────────────────────────────────────────

    public IRelayCommand RefreshNowCommand { get; }
    public IAsyncRelayCommand ApplySafeTuneCommand { get; }
    public IAsyncRelayCommand UndoAllCommand { get; }

    public DashboardViewModel(
        SystemMonitorService monitor,
        IWindowsOptimizerService optimizer,
        IProcessService processService,
        IUndoService undoService,
        SettingsService settings,
        ISensorService sensorService,
        IIntelligenceService intelligence)
    {
        _monitor = monitor;
        _optimizer = optimizer;
        _processService = processService;
        _undoService = undoService;
        _settings = settings;
        _sensorService = sensorService;
        _intelligence = intelligence;

        RefreshNowCommand = new RelayCommand(RefreshNow);
        ApplySafeTuneCommand = new AsyncRelayCommand(ApplySafeTuneAsync);
        UndoAllCommand = new AsyncRelayCommand(UndoAllAsync);
    }

    // ── Monitoring lifecycle ─────────────────────────────────────────────────

    public void StartMonitoring()
    {
        if (_timer != null) return;

        // Capture UI dispatcher for marshaling background results.
        _dispatcherQueue ??= DispatcherQueue.GetForCurrentThread();

        // First refresh happens on a background thread because LibreHardwareMonitor's
        // initial Update() enumerates all hardware sensors and can take 5-15 seconds
        // on rich systems. Doing it inline froze the UI for ~20s on the first visit.
        StatusMessage = "Loading sensors...";
        _ = Task.Run(RefreshOffThread);

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Math.Max(1, _settings.Settings.MetricsRefreshSeconds))
        };
        _timer.Tick += OnTimerTick;
        _timer.Start();
    }

    public void StopMonitoring()
    {
        if (_timer == null) return;
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
        _timer = null;
    }

    // ── Internal helpers ─────────────────────────────────────────────────────

    private void OnTimerTick(object? sender, object e)
    {
        // Skip ticks while a background refresh is still in flight to avoid pile-up.
        if (!_refreshInFlight)
            _ = Task.Run(RefreshOffThread);

        // Run anomaly check every 30 ticks to avoid per-second overhead
        _anomalyCheckCounter++;
        if (_anomalyCheckCounter >= 30)
        {
            _anomalyCheckCounter = 0;
            _ = CheckAnomaliesAsync();
        }
    }

    private void RefreshOffThread()
    {
        if (_refreshInFlight) return;
        _refreshInFlight = true;
        try
        {
            // Gather everything on the background thread — this is the slow work.
            var snap = _monitor.CollectSnapshot();
            var cores = _monitor.GetPerCoreUsage();
            var procs = _processService.GetTopProcesses(10);
            var undoCount = _undoService.Count;

            HardwareSnapshot? sensors = null;
            if (_sensorService.IsAvailable)
            {
                try { sensors = _sensorService.GetSnapshot(); }
                catch { /* sensors are optional */ }
            }

            // Marshal the property updates onto the UI thread.
            _dispatcherQueue?.TryEnqueue(() =>
            {
                ApplySnapshot(snap);
                ApplyCoreUsage(cores);
                ApplyProcesses(procs);
                UndoableChanges = undoCount;
                LastUpdated = snap.Timestamp.ToString("HH:mm:ss");

                if (sensors != null)
                {
                    if (sensors.CpuPackageTemperatureC.HasValue)
                        CpuTempText = $"{sensors.CpuPackageTemperatureC:F0}°C";
                    if (sensors.GpuTemperatureC.HasValue)
                        GpuTempText = $"{sensors.GpuTemperatureC:F0}°C";
                    if (sensors.CpuPowerWatts.HasValue)
                        CpuPowerText = $"{sensors.CpuPowerWatts:F0}W";
                    if (sensors.GpuPowerWatts.HasValue)
                        GpuPowerText = $"{sensors.GpuPowerWatts:F0}W";
                }

                if (!_initialRefreshDone)
                {
                    _initialRefreshDone = true;
                    if (StatusMessage == "Loading sensors...")
                        StatusMessage = "";
                }
            });
        }
        catch (Exception ex)
        {
            _dispatcherQueue?.TryEnqueue(() => StatusMessage = $"Refresh error: {ex.Message}");
        }
        finally
        {
            _refreshInFlight = false;
        }
    }

    private void RefreshNow() => _ = Task.Run(RefreshOffThread);

    private void ApplySnapshot(SystemResource snap)
    {
        CpuUsage = Math.Round(snap.CpuUsagePercentage, 1);
        TotalCores = snap.TotalProcessors > 0 ? snap.TotalProcessors : Environment.ProcessorCount;

        TotalMemoryBytes = snap.TotalPhysicalMemory;
        UsedMemoryBytes = snap.TotalPhysicalMemory - snap.AvailablePhysicalMemory;
        MemoryUsage = TotalMemoryBytes > 0
            ? Math.Round((double)UsedMemoryBytes / TotalMemoryBytes * 100.0, 1)
            : 0;

        GpuUsage = Math.Round(snap.GpuUsagePercentage, 1);

        DiskReadSpeed = snap.DiskReadSpeed;
        DiskWriteSpeed = snap.DiskWriteSpeed;
        // Represent disk activity as a 0-100 scale (cap at 100 MB/s = full bar)
        DiskUsage = Math.Min(100.0, (snap.DiskReadSpeed + snap.DiskWriteSpeed) / (100.0 * 1_048_576) * 100.0);

        NetworkInSpeed = snap.NetworkInSpeed;
        NetworkOutSpeed = snap.NetworkOutSpeed;
        // Represent network activity capped at 125 MB/s (1 Gbps)
        NetworkUsage = Math.Min(100.0, (snap.NetworkInSpeed + snap.NetworkOutSpeed) / (125.0 * 1_048_576) * 100.0);

        // Add to chart history (keep last N seconds as configured)
        ChartHistory.Add(snap);
        var maxHistory = Math.Max(10, _settings.Settings.ChartHistorySeconds);
        while (ChartHistory.Count > maxHistory)
            ChartHistory.RemoveAt(0);

        // Update sparkline histories
        AppendSparkline(CpuHistory, CpuUsage, maxHistory);
        AppendSparkline(MemoryHistory, MemoryUsage, maxHistory);
        AppendSparkline(GpuHistory, GpuUsage, maxHistory);
        AppendSparkline(DiskHistory, DiskUsage, maxHistory);
        AppendSparkline(NetworkHistory, NetworkUsage, maxHistory);

        UpdateHealthScore();
    }

    private static void AppendSparkline(ObservableCollection<double> history, double value, int maxCount)
    {
        history.Add(value);
        while (history.Count > maxCount)
            history.RemoveAt(0);
    }

    private void UpdateHealthScore()
    {
        // Simple weighted score: penalise high CPU/memory/GPU usage
        var score = 100;
        if (CpuUsage > 90) score -= 30;
        else if (CpuUsage > 75) score -= 15;
        else if (CpuUsage > 50) score -= 5;

        if (MemoryUsage > 90) score -= 30;
        else if (MemoryUsage > 75) score -= 15;
        else if (MemoryUsage > 50) score -= 5;

        if (GpuUsage > 90) score -= 20;
        else if (GpuUsage > 75) score -= 10;

        score = Math.Clamp(score, 0, 100);
        HealthScore = score;
        HealthText = score switch
        {
            >= 70 => "System is healthy",
            >= 40 => "System is under load",
            _ => "System is under heavy stress"
        };
    }

    private void ApplyCoreUsage(IReadOnlyList<double> cores)
    {
        // Sync PerCoreUsage collection to match the new values
        for (int i = 0; i < cores.Count; i++)
        {
            if (i < PerCoreUsage.Count)
                PerCoreUsage[i] = cores[i];
            else
                PerCoreUsage.Add(cores[i]);
        }
        while (PerCoreUsage.Count > cores.Count)
            PerCoreUsage.RemoveAt(PerCoreUsage.Count - 1);
    }

    private void ApplyProcesses(IReadOnlyList<ProcessInfo> procs)
    {
        TopProcesses.Clear();
        foreach (var p in procs)
            TopProcesses.Add(p);
    }

    // ── Command implementations ───────────────────────────────────────────────

    private async Task ApplySafeTuneAsync()
    {
        IsBusy = true;
        StatusMessage = "Applying Clean & Light preset…";
        try
        {
            var result = await _optimizer.ApplyProfileAsync("preset-clean");
            StatusMessage = result
                ? "Clean & Light preset applied successfully."
                : "Failed to apply preset.";
            UndoableChanges = _undoService.Count;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task UndoAllAsync()
    {
        IsBusy = true;
        StatusMessage = "Reverting all optimizations…";
        try
        {
            var count = await _optimizer.UndoAllOptimizationsAsync();
            StatusMessage = count > 0
                ? $"Reverted {count} optimization(s)."
                : "Nothing to revert.";
            UndoableChanges = _undoService.Count;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ── Anomaly detection ─────────────────────────────────────────────────────

    private async Task CheckAnomaliesAsync()
    {
        try
        {
            var cpuAnomalies = await _intelligence.DetectAnomaliesAsync(
                CpuHistory.ToList(), "CPU Usage");
            var memAnomalies = await _intelligence.DetectAnomaliesAsync(
                MemoryHistory.ToList(), "Memory Usage");

            var alerts = cpuAnomalies.Concat(memAnomalies).ToList();
            if (alerts.Count > 0)
            {
                // Show the highest-severity alert in the status message
                var top = alerts.OrderByDescending(a => a.Severity).First();
                StatusMessage = $"⚠️ {top.Description}";
            }
        }
        catch (Exception ex)
        {
            EngineLog.Error("Anomaly check failed", ex);
        }
    }

    // ── IDisposable ──────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopMonitoring();
    }
}
