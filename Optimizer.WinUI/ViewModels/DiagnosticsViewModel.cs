using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.Services.Events;

namespace Optimizer.WinUI.ViewModels;

public partial class DiagnosticsViewModel : ObservableObject
{
    private readonly IDiagnosticsService _diagnostics;
    private readonly IDriverDiagnosticsService _driverDiagnostics;
    private readonly IBottleneckDetectorService _bottleneckDetector;
    private readonly NavigationService _navigation;
    private readonly IPredictiveMaintenanceService? _predictive;
    private readonly IEventBus? _eventBus;
    private List<DiagnosticFinding> _allFindings = [];

    // ── Findings ────────────────────────────────────────────────────────────
    [ObservableProperty] private bool isScanning;
    [ObservableProperty] private string progressText = "";
    [ObservableProperty] private string filterSeverity = "All";
    [ObservableProperty] private string filterCategory = "All";
    [ObservableProperty] private DateTime? lastScanTime;
    [ObservableProperty] private int criticalCount;
    [ObservableProperty] private int warningCount;
    [ObservableProperty] private int infoCount;

    // ── Drivers ─────────────────────────────────────────────────────────────
    [ObservableProperty] private bool isDriverScanning;
    [ObservableProperty] private string driverScanStatus = "";

    // ── Bottlenecks ─────────────────────────────────────────────────────────
    [ObservableProperty] private bool isBottleneckRunning;
    [ObservableProperty] private string bottleneckStatus = "";
    [ObservableProperty] private string bottleneckSummary = "";

    // ── Network deep ────────────────────────────────────────────────────────
    [ObservableProperty] private bool isNetworkDeepRunning;
    [ObservableProperty] private string networkDeepStatus = "";

    // ── Predictions ──────────────────────────────────────────────────────────
    [ObservableProperty] private bool isPredictionLoading;
    [ObservableProperty] private string predictionStatus = "";

    public ObservableCollection<DriveSpaceForecast>  DriveForecasts { get; } = [];
    public ObservableCollection<DiskFailureForecast> DiskForecasts  { get; } = [];

    public ObservableCollection<DiagnosticFinding> Findings { get; } = [];
    public ObservableCollection<DriverIssue> DriverIssues { get; } = [];
    public ObservableCollection<ProcessBottleneck> Bottlenecks { get; } = [];
    public ObservableCollection<NetworkDiagnostic> NetworkDiagnostics { get; } = [];

    public List<string> SeverityOptions { get; } = ["All", "Critical", "Warning", "Info"];
    public List<string> CategoryOptions { get; } = ["All", "Performance", "Storage", "Security", "Privacy", "Stability", "Network", "Hardware", "Maintenance"];

    public string CategoryName => "Diagnostics";
    public string CategoryIcon => "🩺";

    /// <summary>True after a scan completed with zero findings (scan ran but everything is clean).</summary>
    public bool IsFindingsEmpty => !IsScanning && _allFindings.Count == 0 && LastScanTime.HasValue;

    public DiagnosticsViewModel(
        IDiagnosticsService diagnostics,
        IDriverDiagnosticsService driverDiagnostics,
        IBottleneckDetectorService bottleneckDetector,
        NavigationService navigation,
        IPredictiveMaintenanceService? predictive = null,
        IEventBus? eventBus = null)
    {
        _diagnostics        = diagnostics;
        _driverDiagnostics  = driverDiagnostics;
        _bottleneckDetector = bottleneckDetector;
        _navigation         = navigation;
        _predictive         = predictive;
        _eventBus           = eventBus;
    }

    /// <summary>Publish a DiagnosticCompleted event so the Activity console reflects the scan.</summary>
    private void PublishScanCompleted(string scanKind)
    {
        var detail = _allFindings.Count == 0
            ? "no issues found"
            : $"{CriticalCount} critical, {WarningCount} warning, {InfoCount} info";
        _eventBus?.Publish(OptimizerEvent.Create(
            OptimizerEventType.DiagnosticCompleted,
            $"{scanKind} scan completed",
            detail,
            new Dictionary<string, string>
            {
                ["findings"] = _allFindings.Count.ToString(),
                ["critical"] = CriticalCount.ToString(),
                ["warning"]  = WarningCount.ToString(),
                ["info"]     = InfoCount.ToString(),
            }));
    }

    // ── Quick / Full scan ────────────────────────────────────────────────────

    [RelayCommand]
    public async Task QuickScanAsync()
    {
        IsScanning = true;
        ProgressText = "Running quick scan...";
        try
        {
            _allFindings = (await _diagnostics.RunQuickScanAsync()).ToList();
            LastScanTime = DateTime.Now;
            UpdateCounts();
            ApplyFilters();
            PublishScanCompleted("Quick");
        }
        finally
        {
            IsScanning = false;
            ProgressText = "";
        }
    }

    [RelayCommand]
    public async Task FullScanAsync()
    {
        IsScanning = true;
        var progress = new Progress<string>(s => ProgressText = s);
        try
        {
            _allFindings = (await _diagnostics.RunFullScanAsync(progress)).ToList();
            LastScanTime = DateTime.Now;
            UpdateCounts();
            ApplyFilters();
            PublishScanCompleted("Full");
        }
        finally
        {
            IsScanning = false;
        }
    }

    // ── Driver scan ──────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task ScanDriversAsync()
    {
        IsDriverScanning = true;
        DriverScanStatus = "Scanning device drivers...";
        DriverIssues.Clear();
        try
        {
            var issues = await _driverDiagnostics.ScanAsync();
            foreach (var issue in issues)
                DriverIssues.Add(issue);
            DriverScanStatus = issues.Count == 0
                ? "No driver issues found."
                : $"Found {issues.Count} issue(s).";
        }
        catch (Exception ex)
        {
            DriverScanStatus = $"Scan failed: {ex.Message}";
        }
        finally
        {
            IsDriverScanning = false;
        }
    }

    // ── Bottleneck detection ─────────────────────────────────────────────────

    [RelayCommand]
    public async Task DetectBottlenecksAsync()
    {
        IsBottleneckRunning = true;
        BottleneckSummary = "";
        Bottlenecks.Clear();
        var progress = new Progress<string>(s => BottleneckStatus = s);
        try
        {
            var report = await _bottleneckDetector.DetectAsync(progress);
            foreach (var b in report.TopOffenders)
                Bottlenecks.Add(b);
            BottleneckSummary = report.Summary;
            BottleneckStatus = $"Completed at {report.GeneratedAt:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            BottleneckStatus = $"Detection failed: {ex.Message}";
        }
        finally
        {
            IsBottleneckRunning = false;
        }
    }

    // ── Network deep diagnostics ─────────────────────────────────────────────

    [RelayCommand]
    public async Task RunNetworkDeepAsync()
    {
        IsNetworkDeepRunning = true;
        NetworkDiagnostics.Clear();
        var progress = new Progress<string>(s => NetworkDeepStatus = s);
        try
        {
            var results = await _diagnostics.RunNetworkDeepAsync(progress);
            foreach (var r in results)
                NetworkDiagnostics.Add(r);
            NetworkDeepStatus = $"Completed {results.Count} test(s).";
        }
        catch (Exception ex)
        {
            NetworkDeepStatus = $"Failed: {ex.Message}";
        }
        finally
        {
            IsNetworkDeepRunning = false;
        }
    }

    // ── Predictions ──────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task LoadPredictionsAsync()
    {
        if (_predictive == null)
        {
            PredictionStatus = "Predictive maintenance service unavailable.";
            return;
        }

        IsPredictionLoading = true;
        PredictionStatus    = "Loading forecasts…";
        DriveForecasts.Clear();
        DiskForecasts.Clear();

        try
        {
            var drives = await _predictive.ForecastDriveSpaceAsync();
            foreach (var d in drives)
                DriveForecasts.Add(d);

            var disks = await _predictive.ForecastDiskHealthAsync();
            foreach (var d in disks)
                DiskForecasts.Add(d);

            PredictionStatus = $"Updated at {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            PredictionStatus = $"Forecast failed: {ex.Message}";
        }
        finally
        {
            IsPredictionLoading = false;
        }
    }

    // ── Display test ─────────────────────────────────────────────────────────

    [RelayCommand]
    public void OpenDisplayTest()
    {
        _navigation.NavigateTo(typeof(Views.DisplayTestPage));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    partial void OnFilterSeverityChanged(string value) => ApplyFilters();
    partial void OnFilterCategoryChanged(string value) => ApplyFilters();
    partial void OnIsScanningChanged(bool value)       => OnPropertyChanged(nameof(IsFindingsEmpty));
    partial void OnLastScanTimeChanged(DateTime? value) => OnPropertyChanged(nameof(IsFindingsEmpty));

    private void UpdateCounts()
    {
        CriticalCount = _allFindings.Count(f => f.Severity == FindingSeverity.Critical);
        WarningCount = _allFindings.Count(f => f.Severity == FindingSeverity.Warning);
        InfoCount = _allFindings.Count(f => f.Severity == FindingSeverity.Info);
    }

    private void ApplyFilters()
    {
        IEnumerable<DiagnosticFinding> filtered = _allFindings;
        if (FilterSeverity != "All" && Enum.TryParse<FindingSeverity>(FilterSeverity, out var sev))
            filtered = filtered.Where(f => f.Severity == sev);
        if (FilterCategory != "All" && Enum.TryParse<FindingCategory>(FilterCategory, out var cat))
            filtered = filtered.Where(f => f.Category == cat);

        Findings.Clear();
        foreach (var f in filtered.OrderByDescending(f => (int)f.Severity))
            Findings.Add(f);
    }
}
