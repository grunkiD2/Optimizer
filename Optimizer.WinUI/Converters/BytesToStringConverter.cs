using Microsoft.UI.Xaml.Data;
using Optimizer.WinUI.Helpers;

namespace Optimizer.WinUI.Converters;

public class BytesToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is long bytes)
            return ByteFormatter.Format(bytes);
        return "0 B";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
