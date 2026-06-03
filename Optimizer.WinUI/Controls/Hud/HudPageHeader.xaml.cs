using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace Optimizer.WinUI.Controls.Hud;

/// <summary>Standard page header: an accent icon tile, title, one-line description, and a right-aligned
/// action slot (the default content property). Gives every page a clear identity and affordances.</summary>
[ContentProperty(Name = nameof(Actions))]
public sealed partial class HudPageHeader : UserControl
{
    public HudPageHeader() => InitializeComponent();

    public string Icon
    {
        get => (string)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(string), typeof(HudPageHeader), new PropertyMetadata(""));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(HudPageHeader), new PropertyMetadata(""));

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }
    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(HudPageHeader), new PropertyMetadata(""));

    public object Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }
    public static readonly DependencyProperty ActionsProperty =
        DependencyProperty.Register(nameof(Actions), typeof(object), typeof(HudPageHeader), new PropertyMetadata(null));
}
