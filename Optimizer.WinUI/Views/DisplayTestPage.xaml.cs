using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Optimizer.WinUI.Services;
using Windows.UI;

namespace Optimizer.WinUI.Views;

public sealed partial class DisplayTestPage : Page
{
    private bool _testActive;

    public DisplayTestPage()
    {
        InitializeComponent();
        KeyDown += Page_KeyDown;
    }

    private void Color_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string colorName)
            ShowColor(colorName);
    }

    private void ColorFill_Tapped(object sender, TappedRoutedEventArgs e)
    {
        ExitColorMode();
    }

    private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_testActive && e.Key == Windows.System.VirtualKey.Escape)
        {
            ExitColorMode();
            e.Handled = true;
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        var nav = App.GetService<NavigationService>();
        if (nav.CanGoBack)
            nav.GoBack();
        else
            nav.NavigateTo(typeof(DiagnosticsPage));
    }

    private void ShowColor(string colorName)
    {
        var color = colorName switch
        {
            "White" => Colors.White,
            "Black" => Color.FromArgb(255, 10, 10, 10),
            "Red"   => Color.FromArgb(255, 239, 68, 68),
            "Green" => Color.FromArgb(255, 34, 197, 94),
            "Blue"  => Color.FromArgb(255, 59, 130, 246),
            _       => Colors.Black
        };

        ColorFill.Background = new SolidColorBrush(color);
        ColorFill.Visibility = Visibility.Visible;
        MenuPanel.Visibility = Visibility.Collapsed;
        _testActive = true;
    }

    private void ExitColorMode()
    {
        ColorFill.Visibility = Visibility.Collapsed;
        MenuPanel.Visibility = Visibility.Visible;
        _testActive = false;
    }
}
