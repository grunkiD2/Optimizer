using System;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;

namespace Optimizer.WinUI.Views;

/// <summary>Generic hub container: a Segmented sub-nav (built from a <see cref="HubConfig"/> passed as
/// the navigation parameter) that swaps the inner frame between section pages.</summary>
public sealed partial class HubPage : Page
{
    public HubPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not HubConfig config) return;

        HubTitle.Text = config.Title.ToUpperInvariant();

        SectionSeg.Items.Clear();
        foreach (var s in config.Sections)
            SectionSeg.Items.Add(new SegmentedItem { Content = s.Label, Tag = s.PageType });

        if (SectionSeg.Items.Count > 0)
            SectionSeg.SelectedIndex = 0; // fires Section_Changed → navigates the first section
    }

    private void Section_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (SectionSeg.SelectedItem is SegmentedItem { Tag: Type pageType })
            SectionFrame.Navigate(pageType, null, new SuppressNavigationTransitionInfo());
    }
}
