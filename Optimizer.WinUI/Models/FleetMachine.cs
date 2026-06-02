using CommunityToolkit.Mvvm.ComponentModel;

namespace Optimizer.WinUI.Models;

public partial class FleetMachine : ObservableObject
{
    [ObservableProperty] private string name = "";
    [ObservableProperty] private string hostName = "";
    [ObservableProperty] private string osVersion = "";
    [ObservableProperty] private string ipAddress = "";
    [ObservableProperty] private string lastSeen = "Never";
    [ObservableProperty] private string status = "Unknown";  // Online, Offline, Unknown
    [ObservableProperty] private string department = "";
    [ObservableProperty] private string owner = "";
    [ObservableProperty] private double healthScore;

    public string StatusColor => Status switch
    {
        "Online"  => "#10B981",
        "Offline" => "#6B7280",
        _         => "#F59E0B"
    };
}
