using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;

namespace Optimizer.WinUI.ViewModels;

public partial class ComplianceViewModel : ObservableObject
{
    private readonly IComplianceService _compliance;

    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private string statusMessage = "";
    [ObservableProperty] private string selectedFramework = "CIS Benchmark";
    [ObservableProperty] private int passCount;
    [ObservableProperty] private int failCount;
    [ObservableProperty] private int naCount;

    public List<string> Frameworks { get; }
    public ObservableCollection<ComplianceCheck> Checks { get; } = [];

    public string CategoryName => "Compliance";
    public string CategoryIcon => "✅";

    public ComplianceViewModel(IComplianceService compliance)
    {
        _compliance      = compliance;
        Frameworks       = compliance.AvailableFrameworks;
        SelectedFramework = Frameworks.FirstOrDefault() ?? "CIS Benchmark";
    }

    // ── Run checks ────────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task RunChecksAsync()
    {
        IsLoading = true;
        StatusMessage = $"Running {SelectedFramework} checks…";
        Checks.Clear();
        PassCount = FailCount = NaCount = 0;
        try
        {
            var results = await _compliance.RunFrameworkAsync(SelectedFramework);
            foreach (var c in results) Checks.Add(c);

            PassCount = results.Count(c => c.Status == ComplianceStatus.Pass);
            FailCount = results.Count(c => c.Status == ComplianceStatus.Fail);
            NaCount   = results.Count(c => c.Status == ComplianceStatus.NotApplicable);

            StatusMessage = $"{PassCount} pass, {FailCount} fail, {NaCount} N/A — {results.Count} total checks.";
        }
        finally { IsLoading = false; }
    }

    // ── Export HTML report ────────────────────────────────────────────────────

    [RelayCommand]
    public async Task ExportReportAsync()
    {
        if (Checks.Count == 0)
        {
            StatusMessage = "Run checks first before exporting.";
            return;
        }
        IsLoading = true;
        try
        {
            var path = await _compliance.ExportAuditReportAsync(Checks.ToList());
            StatusMessage = $"Audit report saved: {path}";
        }
        finally { IsLoading = false; }
    }
}
