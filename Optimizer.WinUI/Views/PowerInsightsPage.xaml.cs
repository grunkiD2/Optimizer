using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optimizer.WinUI.Controls.Hud;
using Optimizer.WinUI.Helpers;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.Services.Power;

namespace Optimizer.WinUI.Views;

/// <summary>Monitor → Power Insights: live per-process attribution + drift history
/// (docs/POWER-INSIGHTS.md §6, compact first cut). Read-only surface over IPowerInsightsService.</summary>
public sealed partial class PowerInsightsPage : Page
{
    private readonly IPowerInsightsService _ppi;
    private readonly ISettingsService _settings;
    private DispatcherTimer? _timer;
    private int _ticks;

    public PowerInsightsPage()
    {
        InitializeComponent();
        _ppi = App.GetService<IPowerInsightsService>();
        _settings = App.GetService<ISettingsService>();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Render();
        _ = RenderDriftAsync();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _timer.Tick += (_, _) =>
        {
            Render();
            if (++_ticks % 6 == 0) _ = RenderDriftAsync(); // drift table every 30 s
        };
        _timer.Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _timer?.Stop();
        _timer = null;
    }

    private void Render()
    {
        if (!_settings.Settings.PpiEnabled)
        {
            PackageTile.Value = "--";
            TopDrainTile.Value = "--";
            SetSingleRow(DrainerList, HudStatus.Neutral, "OFF",
                "Power Insights is disabled — turn it on under Settings → Federation & Automation → Power Insights.");
            return;
        }

        var snap = _ppi.LatestSnapshot;
        if (snap == null)
        {
            SetSingleRow(DrainerList, HudStatus.Info, "WARM-UP",
                "Collecting the first attribution window (~90 s after launch)…");
            return;
        }

        PackageTile.Value = snap.PackageWatts is { } w ? w.ToString("F0") : "--";
        PackageTile.Caption = $"live · {snap.WindowSeconds:F0} s window · {_ppi.LatestContext} context · {snap.AttributedShare:P0} attributed";

        var drainers = _ppi.GetTopDrainers(12);
        var top = drainers.FirstOrDefault(d => !d.Excluded) ?? drainers.FirstOrDefault();
        TopDrainTile.Value = top?.EstimatedWatts.ToString("F1") ?? "--";
        TopDrainTile.Caption = top == null ? "" : top.Name + (top.BaselineW is { } b && b > 0.05 ? $" · {top.EstimatedWatts / b:F1}× baseline" : "");

        DrainerList.Children.Clear();
        if (drainers.Count == 0)
        {
            SetSingleRow(DrainerList, HudStatus.Success, "QUIET", "No process above the 0.5 W tracking floor in the last window.");
            return;
        }
        foreach (var d in drainers)
        {
            var (status, pill) = d.Drift switch
            {
                "excluded" => (HudStatus.Neutral, "EXCL"),
                "learning" => (HudStatus.Info, "LEARNING"),
                "elevated" => (HudStatus.Warning, "ELEVATED"),
                "anomalous" => (HudStatus.Danger, "ANOMALY"),
                _ => (HudStatus.Success, "OK"),
            };
            var name = d.InstanceCount > 1 ? $"{d.Name} (×{d.InstanceCount})" : d.Name;
            var detail = $"{d.EstimatedWatts:F1} W · {d.CpuShare:P1} CPU"
                + (d.BaselineW is { } bw ? $" · baseline {bw:F1} W (z={d.ZScore:F1})" : "");
            var row = Row(status, pill, name, detail);
            row.ContextFlyout = BuildDrainerFlyout(d);
            DrainerList.Children.Add(row);
        }
    }

    private async Task RenderDriftAsync()
    {
        if (!_settings.Settings.PpiEnabled) return;
        try
        {
            var events = await _ppi.GetRecentDriftAsync(hours: 24, limit: 12);
            DispatcherQueue.TryEnqueue(() =>
            {
                DriftTile.Value = events.Count.ToString();
                DriftTile.Caption = events.Count == 0 ? "all quiet"
                    : $"{events.Count(e => e.Classification == "anomalous")} anomalous · {events.Count(e => e.Classification == "elevated")} elevated";
                DriftList.Children.Clear();
                if (events.Count == 0)
                {
                    SetSingleRow(DriftList, HudStatus.Success, "CLEAR", "No drift surfaced in the last 24 hours.");
                    return;
                }
                foreach (var ev in events)
                {
                    var status = ev.Classification == "anomalous" ? HudStatus.Danger : HudStatus.Warning;
                    var when = DateTimeOffset.TryParse(ev.Ts, out var ts) ? ts.ToString("HH:mm") : ev.Ts;
                    var row = Row(status, ev.Classification.ToUpperInvariant(),
                        $"{ev.ProcessName} · {ev.Context} · {when}",
                        $"observed {ev.ObservedW:F1} W vs {ev.BaselineW:F1} W baseline · z={ev.ZScore:F1}");
                    row.ContextFlyout = BuildDriftFlyout(ev);
                    DriftList.Children.Add(row);
                }
            });
        }
        catch { /* page is best-effort */ }
    }

    // ── Row context menus (Batch 3) ──────────────────────────────────────────
    // PpiProcessExclusions was findable in code but uneditable in the UI (audit P2).
    // Right-click a drainer/drift row to exclude/include the process or copy its detail.

    private MenuFlyout BuildDrainerFlyout(PowerDrainerRow d)
    {
        var flyout = new MenuFlyout();

        var details = $"{d.Name} — {d.EstimatedWatts:F1} W"
            + (d.BaselineW is { } bw ? $" (baseline {bw:F1} W, z={d.ZScore:F1})" : "")
            + $", {d.CpuShare:P1} CPU, ×{d.InstanceCount}";

        var excluded = IsExcluded(d.Name);
        var toggle = new MenuFlyoutItem
        {
            Text = excluded ? $"Fjern “{d.Name}” fra ekskluderede" : $"Ekskludér “{d.Name}”",
        };
        toggle.Click += (_, _) => ToggleExclusion(d.Name);
        flyout.Items.Add(toggle);

        var copy = new MenuFlyoutItem { Text = "Kopiér detaljer" };
        copy.Click += (_, _) => RowActions.CopyText(details);
        flyout.Items.Add(copy);

        return flyout;
    }

    private MenuFlyout BuildDriftFlyout(PowerDriftEvent ev)
    {
        var flyout = new MenuFlyout();

        var copy = new MenuFlyoutItem { Text = "Kopiér hændelse" };
        copy.Click += (_, _) => RowActions.CopyText(
            $"{ev.Ts} · {ev.Context} · {ev.ProcessName}: observed {ev.ObservedW:F1} W vs {ev.BaselineW:F1} W baseline (z={ev.ZScore:F1}) [{ev.Classification}]");
        flyout.Items.Add(copy);

        var exclude = new MenuFlyoutItem { Text = $"Ekskludér “{ev.ProcessName}”" };
        exclude.Click += (_, _) => ToggleExclusion(ev.ProcessName);
        flyout.Items.Add(exclude);

        return flyout;
    }

    private bool IsExcluded(string name)
        => _settings.Settings.PpiProcessExclusions
            .Any(e => string.Equals(e, name, StringComparison.OrdinalIgnoreCase));

    private void ToggleExclusion(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var list = _settings.Settings.PpiProcessExclusions;
        var existing = list.FirstOrDefault(e => string.Equals(e, name, StringComparison.OrdinalIgnoreCase));
        if (existing != null) list.Remove(existing);
        else list.Add(name);
        _settings.Save();
        Render(); // EXCL pill + flyout label refresh on the next attribution window
    }

    private static void SetSingleRow(StackPanel panel, HudStatus status, string pill, string text)
    {
        panel.Children.Clear();
        panel.Children.Add(Row(status, pill, text, null));
    }

    private static FrameworkElement Row(HudStatus status, string pill, string title, string? detail)
    {
        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var pillEl = new StatusPill { Status = status, Text = pill, VerticalAlignment = VerticalAlignment.Center };
        var titleEl = new TextBlock
        {
            Text = title,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = Microsoft.UI.Xaml.TextTrimming.CharacterEllipsis,
            Style = (Style)Application.Current.Resources["HudBodyStyle"],
        };
        Grid.SetColumn(pillEl, 0);
        Grid.SetColumn(titleEl, 1);
        grid.Children.Add(pillEl);
        grid.Children.Add(titleEl);
        if (detail != null)
        {
            var detailEl = new TextBlock
            {
                Text = detail,
                VerticalAlignment = VerticalAlignment.Center,
                Style = (Style)Application.Current.Resources["HudCaptionStyle"],
            };
            Grid.SetColumn(detailEl, 2);
            grid.Children.Add(detailEl);
        }
        return grid;
    }
}
