using CommunityToolkit.Mvvm.ComponentModel;

namespace Optimizer.WinUI.Models;

public enum FindingSeverity { Info, Warning, Critical }
public enum FindingCategory { Performance, Storage, Security, Privacy, Stability, Network, Hardware, Maintenance }

public partial class DiagnosticFinding : ObservableObject
{
    [ObservableProperty] private string id = "";
    [ObservableProperty] private string title = "";
    [ObservableProperty] private string description = "";
    [ObservableProperty] private string recommendation = "";
    [ObservableProperty] private FindingSeverity severity;
    [ObservableProperty] private FindingCategory category;
    [ObservableProperty] private bool hasQuickFix;

    public Func<Task<bool>>? QuickFix { get; set; }

    // Display helpers
    public string SeverityBadge => Severity.ToString().ToUpper();
    public string SeverityColor => Severity switch
    {
        FindingSeverity.Info => "#3B82F6",
        FindingSeverity.Warning => "#F59E0B",
        FindingSeverity.Critical => "#EF4444",
        _ => "#6B7280"
    };
    public string CategoryIcon => Category switch
    {
        FindingCategory.Performance => "⚡",
        FindingCategory.Storage => "💾",
        FindingCategory.Security => "🛡️",
        FindingCategory.Privacy => "🔒",
        FindingCategory.Stability => "⚠️",
        FindingCategory.Network => "🌐",
        FindingCategory.Hardware => "🖥️",
        FindingCategory.Maintenance => "🔧",
        _ => "ℹ️"
    };
}
