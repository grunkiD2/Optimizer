namespace Optimizer.WinUI.ViewModels;

public interface ICategoryViewModel
{
    string CategoryName { get; }
    string CategoryIcon { get; }
    int ActiveCount { get; }
    int TotalCount { get; }
}
