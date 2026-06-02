using CommunityToolkit.Mvvm.ComponentModel;

namespace Optimizer.WinUI.Models;

public partial class ConfigTemplate : ObservableObject
{
    [ObservableProperty] private string id = "";
    [ObservableProperty] private string name = "";
    [ObservableProperty] private string description = "";
    [ObservableProperty] private string format = "DSC";  // DSC, Intune, WinGet
    [ObservableProperty] private DateTime createdAt;

    public List<string> AppliedOptimizationIds { get; set; } = [];
    public Dictionary<string, string> RegistryChanges { get; set; } = [];
}
