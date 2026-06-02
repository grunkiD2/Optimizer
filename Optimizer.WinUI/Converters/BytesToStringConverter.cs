using Microsoft.UI.Xaml.Data;

namespace Optimizer.WinUI.Converters;

public class BytesToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is long bytes)
        {
            return bytes switch
            {
                >= 1_073_741_824 => $"{bytes / 1073741824.0:F1} GB",
                >= 1_048_576 => $"{bytes / 1048576.0:F0} MB",
                >= 1024 => $"{bytes / 1024.0:F0} KB",
                _ => $"{bytes} B"
            };
        }
        return "0 B";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
