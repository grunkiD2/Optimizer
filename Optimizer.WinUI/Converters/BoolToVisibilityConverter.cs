using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Optimizer.WinUI.Converters;

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var b = value is bool bv && bv;
        var invert = parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase);
        if (invert) b = !b;

        // When the target type is bool (e.g. IsEnabled), return the bool directly
        if (targetType == typeof(bool)) return b;

        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        var visible = value is Visibility v && v == Visibility.Visible;
        var invert  = parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase);
        return invert ? !visible : visible;
    }
}
