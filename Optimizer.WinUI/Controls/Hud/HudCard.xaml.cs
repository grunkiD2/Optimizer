using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace Optimizer.WinUI.Controls.Hud;

/// <summary>Glass surface container with a 1px hairline border, rounded per the token radius,
/// and an optional micro eyebrow <see cref="Header"/>. Default content property is the body.</summary>
[ContentProperty(Name = nameof(CardContent))]
public sealed partial class HudCard : UserControl
{
    public HudCard()
    {
        InitializeComponent();
        HoverLift.Attach(this, scale: 1.012f, lift: -3f);
    }

    public object CardContent
    {
        get => GetValue(CardContentProperty);
        set => SetValue(CardContentProperty, value);
    }
    public static readonly DependencyProperty CardContentProperty =
        DependencyProperty.Register(nameof(CardContent), typeof(object), typeof(HudCard), new PropertyMetadata(null));

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(string), typeof(HudCard),
            new PropertyMetadata("", (d, _) => ((HudCard)d).RefreshHeaderArea()));

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }
    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(HudCard),
            new PropertyMetadata("", (d, _) => ((HudCard)d).RefreshHeaderArea()));

    private void RefreshHeaderArea()
    {
        bool hasHeader = !string.IsNullOrWhiteSpace(Header);
        bool hasDesc = !string.IsNullOrWhiteSpace(Description);
        HeaderText.Visibility = hasHeader ? Visibility.Visible : Visibility.Collapsed;
        DescText.Visibility = hasDesc ? Visibility.Visible : Visibility.Collapsed;
        HeaderArea.Visibility = (hasHeader || hasDesc) ? Visibility.Visible : Visibility.Collapsed;
    }
}
