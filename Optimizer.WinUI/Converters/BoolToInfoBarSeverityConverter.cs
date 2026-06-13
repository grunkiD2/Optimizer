using System;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;

namespace Optimizer.WinUI.Converters;

/// <summary>true (isError) → InfoBarSeverity.Error, false → Success. Audit Batch 2 status bars.</summary>
public sealed class BoolToInfoBarSeverityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is true ? InfoBarSeverity.Error : InfoBarSeverity.Success;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
