using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Optimizer.WinUI.Converters;

/// <summary>
/// Returns Visible when the integer value equals the integer in ConverterParameter; otherwise Collapsed.
/// </summary>
public class EqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is int intValue && parameter is string paramStr && int.TryParse(paramStr, out int paramValue))
        {
            return intValue == paramValue ? Visibility.Visible : Visibility.Collapsed;
        }

        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
