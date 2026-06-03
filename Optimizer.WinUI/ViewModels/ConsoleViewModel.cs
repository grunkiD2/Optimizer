using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.Services.Events;

namespace Optimizer.WinUI.ViewModels;

public partial class ConsoleViewModel : ObservableObject
{
    private readonly Action<Action> _dispatch;   // marshal onto the UI thread (or run inline in tests)

    [ObservableProperty] private bool showVerboseLogs;

    public ObservableCollection<ConsoleLine> Lines { get; } = [];

    /// <param name="dispatch">Runs an action on the UI thread. In tests, pass <c>a =&gt; a()</c>.</param>
    public ConsoleViewModel(IEventBus bus, Action<Action> dispatch)
    {
        _dispatch = dispatch;

        foreach (var e in bus.RecentEvents)
            Lines.Add(ToLine(e));

        bus.Subscribe(e => _dispatch(() => Lines.Add(ToLine(e))));
        EngineLog.LineWritten += (msg, ex) =>
        {
            if (!ShowVerboseLogs) return;
            _dispatch(() => Lines.Add(new ConsoleLine
            {
                Glyph = ex is null ? "›" : "⨯",
                Text = ex is null ? msg : $"{msg}: {ex.Message}",
                Color = ex is null ? "#9CA3AF" : "#EF4444",
            }));
        };
    }

    [RelayCommand]
    private void Clear() => Lines.Clear();

    private static ConsoleLine ToLine(OptimizerEvent e) => new()
    {
        TimestampLocal = e.TimestampUtc.ToLocalTime(),
        Glyph = GlyphFor(e.Type),
        Text = string.IsNullOrWhiteSpace(e.Detail) ? e.Title : $"{e.Title} — {e.Detail}",
        Color = ColorFor(e.Type),
    };

    private static string GlyphFor(OptimizerEventType t) => t switch
    {
        OptimizerEventType.OptimizationApplied => "✓",
        OptimizerEventType.OptimizationUndone => "↶",
        OptimizerEventType.ProfileApplied => "▣",
        OptimizerEventType.PluginInstalled or OptimizerEventType.PluginEnabled => "⊕",
        OptimizerEventType.AnomalyDetected => "⚠",
        OptimizerEventType.ThresholdCrossed => "▲",
        OptimizerEventType.DiagnosticCompleted => "✓",
        OptimizerEventType.CloudSyncCompleted => "☁",
        _ => "•"
    };

    private static string ColorFor(OptimizerEventType t) => t switch
    {
        OptimizerEventType.AnomalyDetected => "#EF4444",
        OptimizerEventType.ThresholdCrossed => "#F59E0B",
        _ => "#9CA3AF"
    };
}
