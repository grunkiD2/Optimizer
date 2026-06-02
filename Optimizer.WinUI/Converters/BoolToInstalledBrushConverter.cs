using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Optimizer.WinUI.Converters;

/// <summary>Converts IsInstalled bool to green (installed) or gray (not installed) brush.</summary>
public class BoolToInstalledBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var installed = value is bool b && b;
        return installed
            ? new SolidColorBrush(Colors.SeaGreen)
            : new SolidColorBrush(Color.FromArgb(255, 100, 100, 100));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

/// <summary>Converts IsInstalled bool to "INSTALLED" or "NOT INSTALLED" label.</summary>
public class BoolToInstalledTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is bool b && b ? "INSTALLED" : "NOT INSTALLED";

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

/// <summary>Converts IsInstalled bool to "Launch" or "Download" button label.</summary>
public class BoolToActionTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is bool b && b ? "Launch" : "Download";

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
