using CommunityToolkit.Mvvm.ComponentModel;

namespace Optimizer.WinUI.Models;

public partial class Recommendation : ObservableObject
{
    [ObservableProperty] private string id = "";
    [ObservableProperty] private string title = "";
    [ObservableProperty] private string description = "";
    [ObservableProperty] private string actionLabel = "Fix";
    [ObservableProperty] private FindingSeverity severity;
    [ObservableProperty] private FindingCategory category;
    [ObservableProperty] private bool dismissed;
    [ObservableProperty] private float? mlConfidence;

    public Func<Task<bool>>? QuickAction { get; set; }

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

    public string SeverityBadge => Severity.ToString().ToUpper();

    /// <summary>Non-null when ML model has scored this recommendation (0–100 %).</summary>
    public string? MLConfidenceText => MlConfidence.HasValue
        ? $"ML: {MlConfidence.Value * 100:F0}%"
        : null;

    public bool HasMLConfidence => MlConfidence.HasValue;
}
