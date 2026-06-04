using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace Optimizer.WinUI.Services;

public class NavigationService
{
    private Frame? _frame;

    public Frame? Frame
    {
        get => _frame;
        set => _frame = value;
    }

    public bool CanGoBack => _frame?.CanGoBack ?? false;

    public void GoBack()
    {
        if (_frame?.CanGoBack == true)
            _frame.GoBack();
    }

    public bool NavigateTo(Type pageType, object? parameter = null)
    {
        if (_frame == null) return false;
        // Skip a no-op re-nav to the same page — but always navigate when a parameter is supplied
        // (e.g. switching between hubs, which are all the same HubPage type with different configs).
        if (parameter == null && _frame.Content?.GetType() == pageType) return false;

        return _frame.Navigate(pageType, parameter,
            new DrillInNavigationTransitionInfo());
    }
}
