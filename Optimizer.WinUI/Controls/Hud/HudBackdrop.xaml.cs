using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace Optimizer.WinUI.Controls.Hud;

/// <summary>Reusable HUD page backdrop: deep gradient base + cyan/violet color blooms behind the
/// page content (which goes in the default content slot). Used as a migrated page's root element.</summary>
[ContentProperty(Name = nameof(Child))]
public sealed partial class HudBackdrop : UserControl
{
    public HudBackdrop() => InitializeComponent();

    public object Child
    {
        get => GetValue(ChildProperty);
        set => SetValue(ChildProperty, value);
    }
    public static readonly DependencyProperty ChildProperty =
        DependencyProperty.Register(nameof(Child), typeof(object), typeof(HudBackdrop), new PropertyMetadata(null));
}
