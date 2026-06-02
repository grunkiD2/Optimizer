using CommunityToolkit.Mvvm.ComponentModel;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.ViewModels;

public partial class OptimizationCardModel : ObservableObject
{
    public string Id { get; set; } = "";
    public OptimizationInfo Info { get; set; } = new();
    [ObservableProperty] private bool isActive;
    public bool IsElevated { get; set; }
}
