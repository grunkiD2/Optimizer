using CommunityToolkit.Mvvm.ComponentModel;

namespace Optimizer.WinUI.Models;

public partial class SmartInsight : ObservableObject
{
    [ObservableProperty] private string id = "";
    [ObservableProperty] private string title = "";
    [ObservableProperty] private string body = "";
    [ObservableProperty] private string supportingDataText = "";
    [ObservableProperty] private FindingCategory category;
    [ObservableProperty] private DateTime generatedAt;
}
