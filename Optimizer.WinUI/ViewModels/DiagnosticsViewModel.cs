using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;

namespace Optimizer.WinUI.ViewModels;

public partial class DiagnosticsViewModel : ObservableObject
{
    private readonly IDiagnosticsService _diagnostics;
    private List<DiagnosticFinding> _allFindings = [];

    [ObservableProperty] private bool isScanning;
    [ObservableProperty] private string progressText = "";
    [ObservableProperty] private string filterSeverity = "All";
    [ObservableProperty] private string filterCategory = "All";
    [ObservableProperty] private DateTime? lastScanTime;
    [ObservableProperty] private int criticalCount;
    [ObservableProperty] private int warningCount;
    [ObservableProperty] private int infoCount;

    public ObservableCollection<DiagnosticFinding> Findings { get; } = [];

    public List<string> SeverityOptions { get; } = ["All", "Critical", "Warning", "Info"];
    public List<string> CategoryOptions { get; } = ["All", "Performance", "Storage", "Security", "Privacy", "Stability", "Network", "Hardware", "Maintenance"];

    public string CategoryName => "Diagnostics";
    public string CategoryIcon => "🩺";

    public DiagnosticsViewModel(IDiagnosticsService diagnostics)
    {
        _diagnostics = diagnostics;
    }

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
        }
        finally
        {
            IsScanning = false;
        }
    }

    partial void OnFilterSeverityChanged(string value) => ApplyFilters();
    partial void OnFilterCategoryChanged(string value) => ApplyFilters();

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
