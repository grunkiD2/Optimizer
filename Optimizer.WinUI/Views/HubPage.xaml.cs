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

    // When a HubNavTarget supplies a sub-section index, stash it here so the
    // subsequent Section_Changed handler can pass it through to the section page
    // (e.g. PerformancePage's inner Segmented). Cleared after first use — one-shot.
    private int? _pendingSubSection;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        HubConfig? config = null;
        var sectionIndex = 0;
        _pendingSubSection = null;

        // Both call shapes supported:
        //   1) NavigateTo(HubPage, HubConfig)        — original (slim-rail click)
        //   2) NavigateTo(HubPage, HubNavTarget)     — AI-driven hub-aware nav
        if (e.Parameter is HubNavTarget target)
        {
            config = target.Hub;
            sectionIndex = Math.Clamp(target.SectionIndex, 0, target.Hub.Sections.Count - 1);
            _pendingSubSection = target.SubSectionIndex;
        }
        else if (e.Parameter is HubConfig hc)
        {
            config = hc;
        }

        if (config is null) return;

        HubTitle.Text = config.Title.ToUpperInvariant();

        SectionSeg.Items.Clear();
        foreach (var s in config.Sections)
            SectionSeg.Items.Add(new SegmentedItem { Content = s.Label, Tag = s.PageType });

        if (SectionSeg.Items.Count > 0)
            SectionSeg.SelectedIndex = sectionIndex; // fires Section_Changed → navigates the section
    }

    private void Section_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (SectionSeg.SelectedItem is SegmentedItem { Tag: Type pageType })
        {
            // Pass the sub-section hint exactly once; clear so manual section clicks
            // don't carry stale state.
            var param = _pendingSubSection is int v ? (object)v : null;
            _pendingSubSection = null;
            SectionFrame.Navigate(pageType, param, new SuppressNavigationTransitionInfo());
        }
    }
}
