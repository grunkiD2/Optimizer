using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Optimizer.WinUI.Controls.Hud;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.Services.Commands;

namespace Optimizer.WinUI.Views;

/// <summary>The HUD home: a glowing health ring, live CPU/GPU/RAM tiles, what needs attention,
/// and automation status. Reference implementation for the Command Center redesign (Phase 1).</summary>
public sealed partial class CommandCenterPage : Page
{
    private readonly ISystemDataBus _bus;
    private readonly IRecommendationsService _recs;
    private readonly IContextDetectionService _context;
    private readonly ISettingsService _settings;
    private readonly IWindowsOptimizerService _optimizer;
    private readonly NavigationService _nav;
    private readonly IFancontrolStatusService _fancontrol;
    private DispatcherTimer? _fancontrolTimer;

    private const int TrendLength = 40;
    private readonly Queue<double> _cpuTrend = new();
    private readonly Queue<double> _gpuTrend = new();
    private readonly Queue<double> _ramTrend = new();

    public CommandCenterPage()
    {
        InitializeComponent();
        _bus = App.GetService<ISystemDataBus>();
        _recs = App.GetService<IRecommendationsService>();
        _context = App.GetService<IContextDetectionService>();
        _settings = App.GetService<ISettingsService>();
        _optimizer = App.GetService<IWindowsOptimizerService>();
        _nav = App.GetService<NavigationService>();
        _fancontrol = App.GetService<IFancontrolStatusService>();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _bus.SetSensorsActive(true);           // fast cadence for temps while we're on screen
        _bus.MetricsUpdated += OnMetrics;
        if (_bus.LatestMetrics is { } seed) OnMetrics(seed);

        BuildAutomation();
        _ = LoadContextAsync();
        _ = LoadAttentionAsync();

        if (_fancontrol.IsConfigured)
        {
            FancontrolCard.Visibility = Visibility.Visible;
            RefreshFancontrol();
            _fancontrolTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) }; // brain ticks every 5 s
            _fancontrolTimer.Tick += (_, _) => RefreshFancontrol();
            _fancontrolTimer.Start();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _bus.MetricsUpdated -= OnMetrics;
        _bus.SetSensorsActive(false);
        _fancontrolTimer?.Stop();
        _fancontrolTimer = null;
    }

    // ── Live metrics ──────────────────────────────────────────────────────────

    private void OnMetrics(SystemResource m)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            double memTotal = m.TotalPhysicalMemory;
            double memUsed = memTotal > 0 ? memTotal - m.AvailablePhysicalMemory : 0;
            double memPct = memTotal > 0 ? memUsed / memTotal * 100.0 : 0;

            Push(_cpuTrend, m.CpuUsagePercentage);
            CpuTile.Value = m.CpuUsagePercentage.ToString("F0");
            CpuTile.Caption = m.CpuTemperature > 0 ? $"{m.CpuTemperature:F0}°C" : "temp n/a";
            CpuTile.Status = Band(m.CpuUsagePercentage, 70, 90);
            CpuTile.SetTrend(_cpuTrend.ToArray());

            Push(_gpuTrend, m.GpuUsagePercentage);
            GpuTile.Value = m.GpuUsagePercentage.ToString("F0");
            GpuTile.Caption = m.GpuTemperature > 0 ? $"{m.GpuTemperature:F0}°C" : "temp n/a";
            GpuTile.Status = Band(m.GpuUsagePercentage, 70, 90);
            GpuTile.SetTrend(_gpuTrend.ToArray());

            Push(_ramTrend, memPct);
            RamTile.Value = (memUsed / 1_073_741_824.0).ToString("F1");
            RamTile.Caption = $"{memPct:F0}% used";
            RamTile.Status = Band(memPct, 75, 90);
            RamTile.SetTrend(_ramTrend.ToArray());

            double health = Math.Clamp(100.0 - 0.45 * m.CpuUsagePercentage - 0.35 * memPct - 0.20 * m.GpuUsagePercentage, 1, 100);
            if (!_ringInitialized) { _ringInitialized = true; _ = SweepRingAsync(health); }
            else if (!_sweeping) HealthRingControl.Score = health;
        });
    }

    // Entrance sweep: ease the ring from 0 to the first reading, then track live.
    private bool _ringInitialized;
    private bool _sweeping;
    private async Task SweepRingAsync(double target)
    {
        _sweeping = true;
        const int steps = 28;
        for (int i = 1; i <= steps; i++)
        {
            double t = i / (double)steps;
            HealthRingControl.Score = target * (1 - Math.Pow(1 - t, 3)); // easeOutCubic
            await Task.Delay(16);
        }
        HealthRingControl.Score = target;
        _sweeping = false;
    }

    private static void Push(Queue<double> q, double v)
    {
        q.Enqueue(v);
        while (q.Count > TrendLength) q.Dequeue();
    }

    private static HudStatus Band(double v, double warn, double danger) =>
        v >= danger ? HudStatus.Danger : v >= warn ? HudStatus.Warning : HudStatus.Accent;

    // ── Context ───────────────────────────────────────────────────────────────

    private async Task LoadContextAsync()
    {
        try
        {
            var ctx = await _context.DetectContextAsync();
            DispatcherQueue.TryEnqueue(() => CtxText.Text = (ctx ?? "Unknown").ToUpperInvariant());
        }
        catch { /* leave the placeholder */ }
    }

    // ── Needs attention ───────────────────────────────────────────────────────

    private async Task LoadAttentionAsync()
    {
        try
        {
            var recs = await _recs.GenerateAsync();
            DispatcherQueue.TryEnqueue(() =>
            {
                AttentionList.Children.Clear();
                foreach (var r in recs.Take(5))
                    AttentionList.Children.Add(AttentionActionRow(r));
                if (recs.Count == 0)
                    AttentionList.Children.Add(AttentionRow(HudStatus.Success, "OK", "All clear — nothing needs attention."));
            });
        }
        catch { /* best-effort panel */ }
    }

    private void BuildAutomation()
    {
        var s = _settings.Settings;
        AutomationList.Children.Clear();
        AutomationList.Children.Add(AttentionRow(
            s.AutomationPaused ? HudStatus.Warning : HudStatus.Success,
            s.AutomationPaused ? "PAUSED" : "ARMED",
            "Automation engine"));
        AutomationList.Children.Add(AttentionRow(
            s.AutoApplyEnabled ? HudStatus.Success : HudStatus.Neutral,
            s.AutoApplyEnabled ? "ON" : "OFF",
            "Auto-apply optimizations"));
        AutomationList.Children.Add(AttentionRow(
            s.AutoContextSwitchEnabled ? HudStatus.Success : HudStatus.Neutral,
            s.AutoContextSwitchEnabled ? "ON" : "OFF",
            "Auto context switching"));
    }

    private FrameworkElement AttentionRow(HudStatus status, string pill, string title)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        row.Children.Add(new StatusPill { Status = status, Text = pill, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock
        {
            Text = title,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Style = Res<Style>("HudBodyStyle"),
        });
        return row;
    }

    /// <summary>A Needs-Attention row with an inline action button — the command center acts, not just shows.</summary>
    private FrameworkElement AttentionActionRow(Recommendation r)
    {
        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var pill = new StatusPill { Status = SeverityToStatus(r.Severity), Text = r.Severity.ToString().ToUpperInvariant(), VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock { Text = r.Title, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Style = Res<Style>("HudBodyStyle") };
        var btn = new Button
        {
            Content = string.IsNullOrWhiteSpace(r.ActionLabel) ? "Fix" : r.ActionLabel,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(12, 4, 12, 4),
        };
        btn.Click += async (_, _) => await InvokeRecAction(r);

        Grid.SetColumn(pill, 0);
        Grid.SetColumn(title, 1);
        Grid.SetColumn(btn, 2);
        grid.Children.Add(pill);
        grid.Children.Add(title);
        grid.Children.Add(btn);
        return grid;
    }

    private async Task InvokeRecAction(Recommendation r)
    {
        if (r.QuickAction is { } action)
        {
            SetStatus($"Working on “{r.Title}”…");
            try { SetStatus(await action() ? "Done." : "Couldn't apply that automatically."); }
            catch { SetStatus("That action failed."); }
            await LoadAttentionAsync();
            return;
        }
        // No inline fix — take the user to the right section using hub-aware navigation
        // (IPageNavigator) so the slim rail highlights the parent hub and the page lands
        // inside its Segmented sub-nav. Direct navigation via NavigationService would
        // dump the user on a "standalone" page outside its hub structure (Bug C).
        var pageNav = App.GetService<IPageNavigator>();
        pageNav.NavigateTo(CategoryTag(r.Category));
    }

    // Each FindingCategory routes to a hub-aware tag (see HubRouting.KnownTags).
    // Kept as a dictionary so an exhaustiveness test can assert every enum value is mapped
    // and every target tag resolves — silent fall-through to "Recommendations" was the
    // shape of the CategoryTag bug spotted in code review.
    internal static readonly IReadOnlyDictionary<FindingCategory, string> CategoryRoutes =
        new Dictionary<FindingCategory, string>
        {
            [FindingCategory.Storage]     = "Storage",       // Optimize → Storage
            [FindingCategory.Privacy]     = "System",        // Optimize → Privacy & System
            [FindingCategory.Security]    = "Security",      // Protect  → Security
            [FindingCategory.Performance] = "Performance",   // Optimize → CPU & Power
            [FindingCategory.Network]     = "Network",       // Optimize → Network
            [FindingCategory.Hardware]    = "Hardware",      // Monitor  → Sensors & Inventory
            [FindingCategory.Stability]   = "Diagnostics",   // Protect  → Diagnostics
            [FindingCategory.Maintenance] = "Storage",       // Optimize → Storage (cleanup lives there)
        };

    internal static string CategoryTag(FindingCategory cat) =>
        CategoryRoutes.TryGetValue(cat, out var tag) ? tag : "Recommendations";

    // ── Fancontrol federation (read-only — docs/MACHINE-OWNERSHIP.md) ─────────

    private void RefreshFancontrol()
    {
        // State files live on disk; read them off the UI thread, render on it.
        _ = Task.Run(() =>
        {
            var status = _fancontrol.GetStatus();
            DispatcherQueue.TryEnqueue(() => RenderFancontrol(status));
        });
    }

    private void RenderFancontrol(FancontrolStatus? status)
    {
        FancontrolList.Children.Clear();
        if (status?.Brain is not { } b)
        {
            FancontrolList.Children.Add(AttentionRow(HudStatus.Warning, "NO DATA", "Fan brain state not readable."));
            return;
        }

        var (brainStatus, brainPill) =
            b.Alarm ? (HudStatus.Danger, "ALARM")
            : b.Stale ? (HudStatus.Warning, "STALE")
            : !b.LhmOk ? (HudStatus.Warning, b.Mode)
            : (HudStatus.Success, b.Mode);
        FancontrolList.Children.Add(AttentionRow(brainStatus, brainPill,
            $"Coolant {b.Coolant:F1}°C · pump {b.PumpRpm} RPM · demand case {b.CaseDemand}/rad {b.RadDemand} · CPU {b.CpuTemp:F0}°C {b.CpuWatts:F0} W · GPU {b.GpuTemp:F0}°C {b.GpuWatts:F0} W"));

        if (status.Profiles is { } p)
            FancontrolList.Children.Add(AttentionRow(
                p.Stale ? HudStatus.Warning : HudStatus.Accent,
                p.LastAppliedProfile ?? "—",
                $"Display profile · auto-profiler {(p.Enabled ? "on" : "off")} · {p.MappedPrograms} mapped apps"));

        if (status.Sentinel is { } s)
            FancontrolList.Children.Add(AttentionRow(
                s.Stale ? HudStatus.Warning : s.Pass && s.Issues.Count == 0 ? HudStatus.Success : HudStatus.Warning,
                s.Stale ? "STALE" : s.Pass && s.Issues.Count == 0 ? "PASS" : $"{s.Issues.Count} ISSUES",
                $"Hourly health check · last run {s.Timestamp:HH:mm}"));
    }

    // ── Quick actions ─────────────────────────────────────────────────────────

    private async void OptimizeGaming_Click(object sender, RoutedEventArgs e)
    {
        SetStatus("Applying the Gaming profile…");
        try { SetStatus(await _optimizer.ApplyProfileAsync("preset-gaming") ? "Gaming profile applied." : "Couldn't apply the Gaming profile."); }
        catch { SetStatus("Applying the Gaming profile failed."); }
    }

    private async void RunCleanup_Click(object sender, RoutedEventArgs e)
    {
        SetStatus("Clearing temporary files…");
        try { var res = await _optimizer.ApplyOptimizationAsync(OptimizationIds.ClearTemporaryFiles); SetStatus(res.Message); }
        catch { SetStatus("Cleanup failed."); }
        await LoadAttentionAsync();
    }

    private async void ContextDetect_Click(object sender, RoutedEventArgs e)
    {
        SetStatus("Re-detecting context…");
        await LoadContextAsync();
        SetStatus("Context updated.");
    }

    private void SetStatus(string message) => DispatcherQueue.TryEnqueue(() => ActionStatus.Text = message);

    private static HudStatus SeverityToStatus(FindingSeverity sev)
    {
        var name = sev.ToString();
        if (name.Contains("Crit", StringComparison.OrdinalIgnoreCase) || name.Contains("Error", StringComparison.OrdinalIgnoreCase))
            return HudStatus.Danger;
        if (name.Contains("Warn", StringComparison.OrdinalIgnoreCase))
            return HudStatus.Warning;
        return HudStatus.Info;
    }

    private static T Res<T>(string key) => (T)Application.Current.Resources[key];
}
