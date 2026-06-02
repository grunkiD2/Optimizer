using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Optimizer.WinUI.Helpers;

public static class ThemeHelper
{
    public static void ApplyBackdrop(Window window, string material)
    {
        window.SystemBackdrop = material switch
        {
            "Mica" => new MicaBackdrop(),
            "MicaAlt" => new MicaBackdrop { Kind = MicaKind.BaseAlt },
            "Acrylic" => new DesktopAcrylicBackdrop(),
            _ => null
        };
    }

    public static void ApplyTheme(FrameworkElement root, string theme)
    {
        root.RequestedTheme = theme switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
    }
}
