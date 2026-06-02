using Microsoft.UI.Xaml.Data;

namespace Optimizer.WinUI.Converters;

/// <summary>
/// Formats a double to one decimal place (e.g. 12.3456 → "12.3").
/// Returns "—" for null.
/// </summary>
public class OneDecimalConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is double d ? d.ToString("F1") : "—";

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
