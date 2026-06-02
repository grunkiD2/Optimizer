using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Optimizer.WinUI.Models;
using Windows.UI;

namespace Optimizer.WinUI.Controls;

public sealed partial class OptimizationCard : UserControl
{
    // ── Dependency Properties ─────────────────────────────────────────────

    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(string), typeof(OptimizationCard),
            new PropertyMetadata("⚙️", OnDisplayPropertyChanged));

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(OptimizationCard),
            new PropertyMetadata(string.Empty, OnDisplayPropertyChanged));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(OptimizationCard),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.Register(nameof(IsActive), typeof(bool), typeof(OptimizationCard),
            new PropertyMetadata(false, OnDisplayPropertyChanged));

    public static readonly DependencyProperty RequiresAdminProperty =
        DependencyProperty.Register(nameof(RequiresAdmin), typeof(bool), typeof(OptimizationCard),
            new PropertyMetadata(false, OnDisplayPropertyChanged));

    public static readonly DependencyProperty IsElevatedProperty =
        DependencyProperty.Register(nameof(IsElevated), typeof(bool), typeof(OptimizationCard),
            new PropertyMetadata(false, OnDisplayPropertyChanged));

    public static readonly DependencyProperty IsReversibleProperty =
        DependencyProperty.Register(nameof(IsReversible), typeof(bool), typeof(OptimizationCard),
            new PropertyMetadata(true, OnDisplayPropertyChanged));

    public static readonly DependencyProperty RequiresRestartProperty =
        DependencyProperty.Register(nameof(RequiresRestart), typeof(bool), typeof(OptimizationCard),
            new PropertyMetadata(false, OnDisplayPropertyChanged));

    public static readonly DependencyProperty ChangesTextProperty =
        DependencyProperty.Register(nameof(ChangesText), typeof(string), typeof(OptimizationCard),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ProsProperty =
        DependencyProperty.Register(nameof(Pros), typeof(List<string>), typeof(OptimizationCard),
            new PropertyMetadata(new List<string>()));

    public static readonly DependencyProperty ConsProperty =
        DependencyProperty.Register(nameof(Cons), typeof(List<string>), typeof(OptimizationCard),
            new PropertyMetadata(new List<string>()));

    // ── CLR wrappers ──────────────────────────────────────────────────────

    public string Icon
    {
        get => (string)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public bool RequiresAdmin
    {
        get => (bool)GetValue(RequiresAdminProperty);
        set => SetValue(RequiresAdminProperty, value);
    }

    public bool IsElevated
    {
        get => (bool)GetValue(IsElevatedProperty);
        set => SetValue(IsElevatedProperty, value);
    }

    public bool IsReversible
    {
        get => (bool)GetValue(IsReversibleProperty);
        set => SetValue(IsReversibleProperty, value);
    }

    public bool RequiresRestart
    {
        get => (bool)GetValue(RequiresRestartProperty);
        set => SetValue(RequiresRestartProperty, value);
    }

    public string ChangesText
    {
        get => (string)GetValue(ChangesTextProperty);
        set => SetValue(ChangesTextProperty, value);
    }

    public List<string> Pros
    {
        get => (List<string>)GetValue(ProsProperty);
        set => SetValue(ProsProperty, value);
    }

    public List<string> Cons
    {
        get => (List<string>)GetValue(ConsProperty);
        set => SetValue(ConsProperty, value);
    }

    // ── Event ─────────────────────────────────────────────────────────────

    public event EventHandler<bool>? Toggled;

    // ── Computed display properties (used by x:Bind) ──────────────────────

    public Brush IconBackground =>
        IsActive
            ? new SolidColorBrush(Color.FromArgb(0xFF, 0x1E, 0x3A, 0x5F))
            : new SolidColorBrush(Color.FromArgb(0xFF, 0x1F, 0x29, 0x37));

    public Brush TitleForeground =>
        IsActive
            ? new SolidColorBrush(Color.FromArgb(0xFF, 0xE0, 0xE0, 0xE0))
            : new SolidColorBrush(Color.FromArgb(0xFF, 0x9C, 0xA3, 0xAF));

    public Visibility ActiveBadgeVisibility =>
        IsActive ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ShieldVisibility =>
        (RequiresAdmin && !IsElevated) ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ReversibleVisibility =>
        IsReversible ? Visibility.Visible : Visibility.Collapsed;

    public Visibility AdminVisibility =>
        RequiresAdmin ? Visibility.Visible : Visibility.Collapsed;

    public Visibility RestartVisibility =>
        RequiresRestart ? Visibility.Visible : Visibility.Collapsed;

    public double ToggleOpacity =>
        (RequiresAdmin && !IsElevated) ? 0.5 : 1.0;

    // ── Constructor ───────────────────────────────────────────────────────

    public OptimizationCard()
    {
        this.InitializeComponent();
    }

    // ── API for page code-behind ──────────────────────────────────────────

    /// <summary>
    /// Populates all properties from an <see cref="OptimizationInfo"/> and refreshes bindings.
    /// </summary>
    public void LoadFromInfo(OptimizationInfo info, bool isActive, bool isElevated)
    {
        // Suppress toggle events while we programmatically update IsActive
        _suppressToggle = true;

        Icon = GetIconForOptimization(info.Id);
        Title = info.Title;
        Description = info.Summary;
        IsActive = isActive;
        RequiresAdmin = info.RequiresAdmin;
        IsElevated = isElevated;
        IsReversible = info.Reversible;
        RequiresRestart = info.RequiresRestart;
        ChangesText = info.Changes.Count > 0
            ? string.Join("\n", info.Changes)
            : "(no change details available)";
        Pros = new List<string>(info.Pros);
        Cons = new List<string>(info.Cons);

        // Sync the toggle switch without firing the Toggled event
        if (MainToggle.IsOn != isActive)
            MainToggle.IsOn = isActive;

        _suppressToggle = false;

        Bindings.Update();
    }

    // ── Private ───────────────────────────────────────────────────────────

    private bool _suppressToggle;

    private static void OnDisplayPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is OptimizationCard card)
            card.Bindings.Update();
    }

    private void Toggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggle) return;

        var isOn = MainToggle.IsOn;

        // If admin required and not elevated, revert the toggle and bail out
        if (RequiresAdmin && !IsElevated)
        {
            _suppressToggle = true;
            MainToggle.IsOn = IsActive; // revert
            _suppressToggle = false;
            return;
        }

        IsActive = isOn;
        Bindings.Update();
        Toggled?.Invoke(this, isOn);
    }

    /// <summary>Maps optimization IDs to representative emoji icons.</summary>
    private static string GetIconForOptimization(string id) => id switch
    {
        "DisableBackgroundApps" => "🔕",
        "DisableAnimations" => "✨",
        "DisableVisualEffects" => "🎨",
        "OptimizePowerSettings" => "⚡",
        "AdjustPageFileSize" => "📄",
        "DisableTelemetry" => "📡",
        "DisableWindowsSearch" => "🔍",
        "DisableSuperFetch" => "💾",
        "DisableWindowsUpdate" => "🔄",
        "OptimizeNetworkSettings" => "🌐",
        "DisableIPv6" => "🔒",
        "OptimizeDNS" => "📶",
        "DisableNetworkThrottling" => "🚀",
        "CleanTempFiles" => "🧹",
        "DisableHibernation" => "💤",
        "EnableNTFSCompression" => "📦",
        "DisableStartupItems" => "🚫",
        "DisableTaskSchedulerItems" => "📅",
        _ => "⚙️"
    };
}
