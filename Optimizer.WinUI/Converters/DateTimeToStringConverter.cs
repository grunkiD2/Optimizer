using Microsoft.UI.Xaml.Data;

namespace Optimizer.WinUI.Converters;

public class DateTimeToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is DateTime dt)
        {
            // Show "(never)" for the default/unset value
            if (dt == default || dt.Year < 2000)
                return "never";
            return dt.ToLocalTime().ToString("MMM d, yyyy  h:mm tt");
        }
        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
