using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optimizer.WinUI.Controls;
using Optimizer.WinUI.Controls.Hud;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;
using Windows.UI;

namespace Optimizer.WinUI.Views;

/// <summary>
/// Etape 1: Monitor → Fancontrol — the federation hub. Live status (5 s), history charts over
/// the ingested telemetry copy, a live tail of the engine's events.jsonl, and an alarm banner
/// whose actions go through ctl.ps1 only (docs/MACHINE-OWNERSHIP.md).
/// </summary>
public sealed partial class FancontrolPage : Page
{
    private static readonly Color CoolantColor = Color.FromArgb(255, 0x38, 0xBD, 0xF8); // accent cyan
    private static readonly Color CpuColor = Color.FromArgb(255, 0xF5, 0x9E, 0x0B);     // warning amber
    private static readonly Color GpuColor = Color.FromArgb(255, 0x34, 0xD3, 0x99);     // success green
    private static readonly Color CpuTempColor = Color.FromArgb(255, 0xF8, 0x71, 0x71); // danger red
    private static readonly Color GpuTempColor = Color.FromArgb(255, 0xA7, 0x8B, 0xFA); // violet

    private readonly IFancontrolStatusService _status;
    private readonly IFancontrolTelemetryService _telemetry;
    private readonly IFancontrolEventsService _events;
    private readonly IFancontrolCommandService _commands;

    private DispatcherTimer? _timer;
    private int _ticks;
    private bool _commandBusy;
    private readonly Queue<double> _coolTrend = new(), _pumpTrend = new(), _cpuTrend = new(), _gpuTrend = new();

    public FancontrolPage()
    {
        // Services FIRST: InitializeComponent applies SelectedIndex, which fires
        // RangeBox_SelectionChanged before the constructor body would otherwise finish.
        _status = App.GetService<IFancontrolStatusService>();
        _telemetry = App.GetService<IFancontrolTelemetryService>();
        _events = App.GetService<IFancontrolEventsService>();
        _commands = App.GetService<IFancontrolCommandService>();
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_status.IsConfigured)
        {
            SetSingleRow(StatusList, HudStatus.Neutral, "OFF",
                "Fancontrol federation not configured — set FancontrolStateDir under Settings.");
            return;
        }
        RefreshStatus();
        RefreshLog();
        _ = RefreshHistoryAsync();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _timer.Tick += (_, _) =>
        {
            RefreshStatus();
            RefreshLog();
            if (++_ticks % 12 == 0) _ = RefreshHistoryAsync();   // charts every 60 s
        };
        _timer.Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _timer?.Stop();
        _timer = null;
    }

    // ── Live status (5 s) ─────────────────────────────────────────────────────

    private void RefreshStatus()
        => _ = Task.Run(() =>
        {
            var st = _status.GetStatus();             // file I/O off the UI thread
            DispatcherQueue.TryEnqueue(() => RenderStatus(st));
        });

    private void RenderStatus(FancontrolStatus? st)
    {
        StatusList.Children.Clear();
        if (st?.Brain is not { } b)
        {
            CoolantTile.Value = PumpTile.Value = CpuTile.Value = GpuTile.Value = "--";
            SetSingleRow(StatusList, HudStatus.Warning, "NO DATA", "brain_state.json missing/unreadable.");
            AlarmBar.IsOpen = false;
            return;
        }

        CoolantTile.Value = b.Coolant is { } cool ? cool.ToString("F1") : "--";
        CoolantTile.Caption = $"demand case {b.CaseDemand?.ToString() ?? "-"} % · rad {b.RadDemand?.ToString() ?? "-"} %";
        CoolantTile.Status = b.Alarm ? HudStatus.Danger : HudStatus.Accent;
        PushTrend(_coolTrend, b.Coolant, CoolantTile);

        PumpTile.Value = b.PumpRpm?.ToString() ?? "--";
        PumpTile.Caption = "AIO pump (50 % flat by design)";
        PushTrend(_pumpTrend, b.PumpRpm, PumpTile);

        CpuTile.Value = b.CpuWatts is { } cw ? cw.ToString("F0") : "--";
        CpuTile.Caption = $"{b.CpuTemp?.ToString("F0") ?? "-"} °C · load {b.CpuLoad?.ToString("F0") ?? "-"} %";
        PushTrend(_cpuTrend, b.CpuWatts, CpuTile);

        GpuTile.Value = b.GpuWatts is { } gw ? gw.ToString("F0") : "--";
        GpuTile.Caption = $"{b.GpuTemp?.ToString("F0") ?? "-"} °C · load {b.GpuLoad?.ToString("F0") ?? "-"} %";
        PushTrend(_gpuTrend, b.GpuWatts, GpuTile);

        var (brainStatus, brainPill) =
            b.Alarm ? (HudStatus.Danger, "ALARM")
            : b.Stale ? (HudStatus.Warning, "STALE")
            : !b.LhmOk ? (HudStatus.Warning, b.Mode)
            : (HudStatus.Success, b.Mode);
        var apps = b.RunningApps.Count > 0 ? string.Join(", ", b.RunningApps) : "none";
        StatusList.Children.Add(Row(brainStatus, brainPill,
            $"Brain v{b.SchemaVersion?.ToString() ?? "?"} · mapped apps running: {apps}",
            $"night {(b.Night ? "on" : "off")} · game {(b.Game ? "on" : "off")} · LHM {(b.LhmOk ? "ok" : "DOWN")}"));

        if (st.Profiles is { } p)
            StatusList.Children.Add(Row(p.Stale ? HudStatus.Warning : HudStatus.Success,
                p.LastAppliedProfile ?? "(none)",
                $"Profile (fgwatch{(p.Stale ? " — STALE" : "")}) · foreground: {p.ForegroundExe ?? "-"}",
                $"{p.MappedPrograms} mapped · veto: {(p.VetoApps.Count > 0 ? string.Join(", ", p.VetoApps) : "none")}"));

        if (st.Sentinel is { } s)
            StatusList.Children.Add(Row(
                s.Pass ? HudStatus.Success : HudStatus.Danger,
                s.Pass ? "PASS" : "FAIL",
                $"Sentinel ({s.Timestamp:HH:mm}{(s.Stale ? " — STALE" : "")}) · coolant avg {s.CoolantAvg?.ToString("F1") ?? "-"} / max {s.CoolantMax?.ToString("F1") ?? "-"} °C",
                s.Issues.Count > 0 ? string.Join(" | ", s.Issues) : "no issues"));

        UpdateAlarmBar(b, st.Sentinel);
    }

    private void UpdateAlarmBar(FancontrolBrainStatus b, FancontrolSentinelStatus? s)
    {
        if (_commandBusy) return;   // don't overwrite an in-flight command result
        if (b.Alarm)
        {
            AlarmBar.Severity = InfoBarSeverity.Error;
            AlarmBar.Title = "FanControl ALARM active";
            AlarmBar.Message = $"All fans at 100 % — CPU {b.CpuTemp:F0} °C · GPU {b.GpuTemp:F0} °C · coolant {b.Coolant:F1} °C.";
            AlarmBar.IsOpen = true;
        }
        else if (s is { Pass: false })
        {
            AlarmBar.Severity = InfoBarSeverity.Warning;
            AlarmBar.Title = $"Sentinel: {s.Issues.Count} issue(s)";
            AlarmBar.Message = string.Join(" | ", s.Issues.Take(3));
            AlarmBar.IsOpen = true;
        }
        else
        {
            AlarmBar.IsOpen = false;
        }
    }

    private static void PushTrend(Queue<double> q, double? v, StatTile tile)
    {
        if (v is not { } val) return;
        q.Enqueue(val);
        while (q.Count > 40) q.Dequeue();
        tile.SetTrend(q.ToArray());
    }

    // ── Alarm actions — through ctl.ps1 only ─────────────────────────────────

    private async void AckButton_Click(object sender, RoutedEventArgs e)
        => await RunCommandAsync(() => _commands.AckAlertsAsync("via Fancontrol-hub"));

    private async void RestartBrainButton_Click(object sender, RoutedEventArgs e)
        => await RunCommandAsync(() => _commands.RestartBrainAsync());

    private async Task RunCommandAsync(Func<Task<CtlResult>> command)
    {
        if (_commandBusy) return;
        _commandBusy = true;
        AckButton.IsEnabled = RestartBrainButton.IsEnabled = false;
        AlarmBar.Message = "Working — the engine verifies fresh state before answering…";
        try
        {
            var r = await command();
            AlarmBar.Message = (r.Success ? "OK: " : "FAILED: ") + r.Output;
        }
        catch (Exception ex) { AlarmBar.Message = "FAILED: " + ex.Message; }
        finally
        {
            _commandBusy = false;
            AckButton.IsEnabled = RestartBrainButton.IsEnabled = true;
            RefreshStatus();
        }
    }

    // ── History charts (60 s + range change) ─────────────────────────────────

    private void RangeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        _ = RefreshHistoryAsync();
    }

    private async Task RefreshHistoryAsync()
    {
        var hours = RangeBox.SelectedIndex switch { 0 => 1.0, 1 => 6.0, _ => 24.0 };
        try
        {
            var pts = await _telemetry.GetHistoryAsync(hours, 300);
            DispatcherQueue.TryEnqueue(() => RenderCharts(pts, hours));
        }
        catch { /* best-effort surface */ }
    }

    private void RenderCharts(List<FancontrolHistoryPoint> pts, double hours)
    {
        HistoryInfo.Text = $"{pts.Count} points over {hours:F0} h (ingested copy of the brain's 5 s telemetry)";
        ChartsHost.Children.Clear();
        if (pts.Count < 2)
        {
            ChartsHost.Children.Add(new TextBlock
            {
                Text = "Not enough telemetry in this window yet.",
                Style = (Style)Application.Current.Resources["HudCaptionStyle"],
            });
            return;
        }
        AddChart("COOLANT °C", pts.Select(p => p.Coolant), CoolantColor);
        AddChart("CPU PACKAGE W", pts.Select(p => p.CpuWatts), CpuColor);
        AddChart("GPU PACKAGE W", pts.Select(p => p.GpuWatts), GpuColor);
        AddChart("CPU TEMP °C", pts.Select(p => p.CpuTemp), CpuTempColor);
        AddChart("GPU TEMP °C", pts.Select(p => p.GpuTemp), GpuTempColor);
    }

    private void AddChart(string label, IEnumerable<double?> raw, Color color)
    {
        var vals = raw.ToList();
        var present = vals.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        if (present.Count < 2) return;
        double min = present.Min(), max = present.Max();
        var range = Math.Max(1e-9, max - min);

        // Carry the last value across NULL gaps (per-field staleness in the source) so the
        // line doesn't dive to zero; SparklineChart expects a 0–100 series.
        var series = new ObservableCollection<double>();
        var last = present[0];
        foreach (var v in vals)
        {
            if (v is { } x) last = x;
            series.Add((last - min) / range * 100.0);
        }

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var labelEl = new TextBlock { Text = label, Style = (Style)Application.Current.Resources["HudCaptionStyle"] };
        var rangeEl = new TextBlock
        {
            Text = $"min {min:F1} · max {max:F1}",
            Style = (Style)Application.Current.Resources["HudCaptionStyle"],
        };
        Grid.SetColumn(rangeEl, 1);
        header.Children.Add(labelEl);
        header.Children.Add(rangeEl);

        ChartsHost.Children.Add(new StackPanel
        {
            Spacing = 4,
            Children =
            {
                header,
                new SparklineChart { Values = series, LineColor = color, Height = 72, HorizontalAlignment = HorizontalAlignment.Stretch },
            },
        });
    }

    // ── Live log (5 s) ───────────────────────────────────────────────────────

    private void RefreshLog()
        => _ = Task.Run(() =>
        {
            var events = _events.ReadTail(60);
            DispatcherQueue.TryEnqueue(() => RenderLog(events));
        });

    private void RenderLog(IReadOnlyList<FancontrolEvent> events)
    {
        LogList.Children.Clear();
        if (events.Count == 0)
        {
            SetSingleRow(LogList, HudStatus.Neutral, "EMPTY", "No events readable from events.jsonl.");
            return;
        }
        foreach (var ev in events)
        {
            var status = ev.Msg.Contains("FEJL", StringComparison.OrdinalIgnoreCase)
                         || ev.Msg.Contains("ALERT", StringComparison.OrdinalIgnoreCase)
                         || ev.Msg.Contains("ABORT", StringComparison.OrdinalIgnoreCase)
                ? HudStatus.Danger
                : ev.Src switch
                {
                    "brain" => HudStatus.Accent,
                    "sentinel" => HudStatus.Warning,
                    "ctl" => HudStatus.Info,
                    _ => HudStatus.Neutral,
                };
            LogList.Children.Add(Row(status, ev.Src, ev.Msg, ev.Ts.ToString("HH:mm:ss")));
        }
    }

    // ── Shared row helpers (PowerInsightsPage pattern) ───────────────────────

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
            TextTrimming = TextTrimming.CharacterEllipsis,
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
