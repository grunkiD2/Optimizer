using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace Optimizer.WinUI.Converters;

public class HealthScoreToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is int score)
        {
            return score switch
            {
                >= 70 => new SolidColorBrush(ColorHelper.FromArgb(255, 74, 222, 128)),   // green
                >= 40 => new SolidColorBrush(ColorHelper.FromArgb(255, 251, 191, 36)),   // yellow
                _ => new SolidColorBrush(ColorHelper.FromArgb(255, 248, 113, 113))       // red
            };
        }
        return new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
