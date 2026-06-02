using Microsoft.UI.Xaml;
using Optimizer.WinUI.Helpers;

namespace Optimizer.WinUI.Services;

public class ThemeService : IThemeService
{
    private Window? _window;

    public void Initialize(Window window) => _window = window;

    public void ApplyTheme(string theme)
    {
        if (_window?.Content is FrameworkElement root)
            ThemeHelper.ApplyTheme(root, theme);
    }

    public void ApplyBackdrop(string material)
    {
        if (_window != null)
            ThemeHelper.ApplyBackdrop(_window, material);
    }
}
