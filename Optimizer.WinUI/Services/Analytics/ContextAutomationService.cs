using Microsoft.Extensions.Hosting;
using Optimizer.WinUI.Services.Data;

namespace Optimizer.WinUI.Services.Analytics;

/// <summary>
/// Watches the detected context and, when it changes, auto-applies the highest-confidence
/// profile learned for that context — but only if the user has opted in and the confidence
/// clears the configured threshold. Every switch is recorded so a quick manual override is
/// learned as a failure (via <see cref="IProfileContextService"/>).
/// </summary>
public class ContextAutomationService(
    IContextDetectionService contextDetection,
    IProfileContextService profileContext,
    IProfileService profiles,
    ISettingsService settings) : IHostedService, IDisposable
{
    private Timer? _timer;
    private string? _lastContext;
    private string? _lastAppliedProfileId;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Evaluate every 30s, starting 30s after launch.
        _timer = new Timer(async void (_) =>
        {
            try { await EvaluateAsync(); }
            catch (Exception ex) { EngineLog.Error("Context automation tick failed", ex); }
        }, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Dispose();
        _timer = null;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _timer?.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Internal: one evaluation cycle. Exposed for testing-friendly invocation.</summary>
    public async Task EvaluateAsync()
    {
        var s = settings.Settings;
        if (s.AutomationPaused || !s.AutoContextSwitchEnabled) return;

        var context = await contextDetection.DetectContextAsync();

        // Only act on an actual context change.
        if (context == _lastContext) return;
        _lastContext = context;
        if (context == "Unknown") return;

        // Pick the best-known profile for this context.
        var best = (await profileContext.GetBestProfilesForContextAsync(context, 1)).FirstOrDefault();
        if (best is null) return;

        // Confidence = success rate weighted by observation volume (saturates at 5 applies).
        var confidence = best.SuccessRate * Math.Min(1.0, best.ApplyCount / 5.0);
        if (confidence < s.AutoContextSwitchConfidence) return;

        // Don't re-apply the same profile back-to-back.
        if (best.ProfileId == _lastAppliedProfileId) return;

        try
        {
            var ok = await profiles.ApplyPresetAsync(best.ProfileId);
            await profileContext.RecordApplicationAsync(best.ProfileId, context);
            _lastAppliedProfileId = best.ProfileId;
            EngineLog.Write(
                $"Auto-switched to profile '{best.ProfileId}' for {context} " +
                $"(confidence {confidence:P0}, ok={ok})");
        }
        catch (Exception ex)
        {
            EngineLog.Error("Auto context switch failed", ex);
        }
    }
}
