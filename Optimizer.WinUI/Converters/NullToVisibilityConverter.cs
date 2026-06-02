using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Optimizer.WinUI.Converters;

/// <summary>
/// Returns Visible when the value is non-null, Collapsed when null.
/// Pass parameter="Invert" to reverse the logic.
/// </summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool isNull = value is null;
        bool invert = parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase);
        return (isNull ^ invert) ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
