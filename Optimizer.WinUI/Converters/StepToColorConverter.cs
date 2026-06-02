using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace Optimizer.WinUI.Converters;

/// <summary>
/// Returns the accent brush when the current step (value) equals the step index (parameter),
/// otherwise returns a subtle secondary brush.
/// </summary>
public class StepToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is int currentStep && parameter is string paramStr && int.TryParse(paramStr, out int stepIndex))
        {
            if (currentStep == stepIndex)
            {
                // Active step: use system accent color
                return (SolidColorBrush)Microsoft.UI.Xaml.Application.Current.Resources["SystemControlForegroundAccentBrush"];
            }
        }

        // Inactive step: subtle secondary color
        return new SolidColorBrush(Colors.Gray) { Opacity = 0.4 };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
