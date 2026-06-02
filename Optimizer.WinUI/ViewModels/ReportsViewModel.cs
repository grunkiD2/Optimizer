using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Optimizer.WinUI.Services;

namespace Optimizer.WinUI.ViewModels;

public partial class ReportsViewModel : ObservableObject
{
    private readonly IReportService _reportService;

    // ── UI state ──────────────────────────────────────────────────────────────
    [ObservableProperty] private ReportType   selectedReportType   = ReportType.SystemSnapshot;
    [ObservableProperty] private ReportFormat selectedReportFormat = ReportFormat.Text;
    [ObservableProperty] private string       previewContent       = "";
    [ObservableProperty] private string       statusMessage        = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool isBusy;
    [ObservableProperty] private bool         hasContent;

    public bool IsNotBusy => !IsBusy;
    [ObservableProperty] private string       lastSavedPath        = "";

    private GeneratedReport? _lastReport;

    // ── Collections for pickers ───────────────────────────────────────────────
    public ObservableCollection<ReportTypeItem> ReportTypes { get; } =
    [
        new("System Snapshot",   ReportType.SystemSnapshot,  "Hardware overview, OS info and storage status"),
        new("Health Report",     ReportType.HealthReport,    "Diagnostic findings and recommended fixes"),
        new("Optimization Log",  ReportType.OptimizationLog, "Full history of applied optimizations"),
        new("Full Report",       ReportType.FullReport,      "Everything combined into one document"),
    ];

    public List<string> FormatOptions { get; } = ["Text", "HTML", "JSON"];

    [ObservableProperty] private string selectedFormatLabel = "Text";

    public string CategoryName => "Reports";
    public string CategoryIcon => "";

    public ReportsViewModel(IReportService reportService)
    {
        _reportService = reportService;
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task GenerateAsync()
    {
        IsBusy       = true;
        StatusMessage = "Generating report...";
        HasContent   = false;
        _lastReport  = null;

        try
        {
            var format = SelectedFormatLabel switch
            {
                "HTML" => ReportFormat.Html,
                "JSON" => ReportFormat.Json,
                _      => ReportFormat.Text
            };

            SelectedReportFormat = format;
            _lastReport  = await _reportService.GenerateAsync(SelectedReportType, format);
            PreviewContent = _lastReport.Content;
            HasContent   = true;
            StatusMessage = $"{_lastReport.Title} generated ({PreviewContent.Length:N0} chars)";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            EngineLog.Error("Report generation failed", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_lastReport == null) return;

        IsBusy        = true;
        StatusMessage = "Saving...";

        try
        {
            var path = await _reportService.SaveReportAsync(_lastReport);
            LastSavedPath = path;
            StatusMessage = $"Saved to {path}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save failed: {ex.Message}";
            EngineLog.Error("Report save failed", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSelectedFormatLabelChanged(string value)
    {
        SelectedReportFormat = value switch
        {
            "HTML" => ReportFormat.Html,
            "JSON" => ReportFormat.Json,
            _      => ReportFormat.Text
        };
    }
}

/// <summary>Display wrapper for report-type picker cards.</summary>
public record ReportTypeItem(string Name, ReportType Type, string Description);
