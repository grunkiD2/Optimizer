using CommunityToolkit.Mvvm.ComponentModel;

namespace Optimizer.WinUI.Models;

public partial class WindowsServiceInfo : ObservableObject
{
    [ObservableProperty] private string serviceName = "";
    [ObservableProperty] private string displayName = "";
    [ObservableProperty] private string description = "";
    [ObservableProperty] private string status = "";         // Running, Stopped, Paused
    [ObservableProperty] private string startupType = "";   // Automatic, Manual, Disabled, AutoDelayed
    [ObservableProperty] private bool canStop;

    // Recommendation: Safe, Caution, Critical, Unknown
    public string Recommendation { get; set; } = "Unknown";
    public string RecommendationReason { get; set; } = "";
}
