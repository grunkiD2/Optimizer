using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Optimizer.WinUI.Converters;

/// <summary>
/// Converts a hex color string (e.g. "#3B82F6") to a SolidColorBrush.
/// Returns a transparent brush for null/empty/unparseable input.
/// </summary>
public class StringToSolidColorBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string hex && hex.Length >= 7 && hex[0] == '#')
        {
            try
            {
                var r = System.Convert.ToByte(hex.Substring(1, 2), 16);
                var g = System.Convert.ToByte(hex.Substring(3, 2), 16);
                var b = System.Convert.ToByte(hex.Substring(5, 2), 16);
                return new SolidColorBrush(ColorHelper.FromArgb(255, r, g, b));
            }
            catch { }
        }
        return new SolidColorBrush(Colors.Transparent);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
