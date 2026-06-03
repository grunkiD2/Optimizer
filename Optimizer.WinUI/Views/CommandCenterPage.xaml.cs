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

namespace Optimizer.WinUI.Views;

/// <summary>The HUD home: a glowing health ring, live CPU/GPU/RAM tiles, what needs attention,
/// and automation status. Reference implementation for the Command Center redesign (Phase 1).</summary>
public sealed partial class CommandCenterPage : Page
{
    private readonly ISystemDataBus _bus;
    private readonly IRecommendationsService _recs;
    private readonly IContextDetectionService _context;
    private readonly ISettingsService _settings;

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
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _bus.MetricsUpdated -= OnMetrics;
        _bus.SetSensorsActive(false);
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

            double health = 100.0 - 0.45 * m.CpuUsagePercentage - 0.35 * memPct - 0.20 * m.GpuUsagePercentage;
            HealthRingControl.Score = Math.Clamp(health, 1, 100);
        });
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
                    AttentionList.Children.Add(AttentionRow(SeverityToStatus(r.Severity), r.Severity.ToString().ToUpperInvariant(), r.Title));
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
