using Microsoft.Extensions.Hosting;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

/// <summary>
/// Profil 2.0 P2.0-d — the automatic preset FOLLOWER (the chroma-pattern on lastApplied).
///
/// Watches the Fancontrol system's active display profile (fgwatch_state.json → lastApplied,
/// refreshed every few seconds) and, when it changes, looks the profile up in profiles.json: if it
/// carries an "optimizer" preset-link, the linked Optimizer preset is applied — with the handlers'
/// built-in undo, EngineLog only (no toast spam), and the audit-Batch-1 powercfg ownership gate that
/// lives inside the apply path (OptimizePowerSettings is refused on this federated machine; this
/// follower also passes includeDestructive:false so destructive optimizations are skipped entirely).
///
/// Three gates, all by construction:
///   • manual-wins-always: lastApplied IS the truth regardless of who set it (manual button, fgwatch
///     auto, or ctl); the follower never competes — it just mirrors.
///   • idempotent: the same preset is never re-applied (switching between two profiles that link the
///     same preset is a no-op; a profile re-asserted without change is a no-op).
///   • opt-in: does nothing unless AppSettings.FancontrolFollowerEnabled is true (read live each tick,
///     so the Settings toggle takes effect without a restart). Ship-dark default.
///
/// Read-only toward Fancontrol (docs/MACHINE-OWNERSHIP.md): it reads the state contract and applies
/// Optimizer-OWNED Windows presets; it never writes Fancontrol state.
/// </summary>
public class FancontrolProfileFollowerService : IHostedService, IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30); // let the host + ProfileService.Load settle

    private readonly IFancontrolStatusService _status;
    private readonly IFancontrolCommandService _commands;
    private readonly IProfileService _profiles;
    private readonly Func<bool> _isEnabled;

    private Timer? _timer;
    private int _running; // re-entrancy guard (Interlocked)

    // The lastApplied we have already reacted to, and the preset we drove it to (idempotency baseline).
    private string? _lastSeenProfile;
    private string? _lastAppliedPreset;

    public FancontrolProfileFollowerService(
        IFancontrolStatusService status,
        IFancontrolCommandService commands,
        IProfileService profiles,
        Func<bool> isEnabled)
    {
        _status = status;
        _commands = commands;
        _profiles = profiles;
        _isEnabled = isEnabled;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // The timer always runs; the per-tick enabled-check is what gates the work, so the user can
        // toggle the follower on/off live without an app restart.
        _timer = new Timer(async _ => await TickSafeAsync(), null, StartupDelay, PollInterval);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Dispose();
        _timer = null;
        return Task.CompletedTask;
    }

    public void Dispose() => _timer?.Dispose();

    private async Task TickSafeAsync()
    {
        if (Interlocked.Exchange(ref _running, 1) == 1) return; // a slow apply must not overlap the next tick
        try { await FollowOnceAsync(); }
        catch (Exception ex) { EngineLog.Error("[FancontrolFollower] follow tick failed", ex); }
        finally { Interlocked.Exchange(ref _running, 0); }
    }

    /// <summary>
    /// One follow step. Internal for unit tests. Returns the preset id it applied this step, or null
    /// when nothing was applied (disabled / unconfigured / no change / no link / unknown preset /
    /// idempotent / first-observation seed).
    /// </summary>
    internal async Task<string?> FollowOnceAsync(CancellationToken ct = default)
    {
        if (!_isEnabled() || !_status.IsConfigured || !_commands.IsConfigured) return null;

        // Don't drive Windows changes off a dead/stale federation: fgwatch refreshes every ~3 s, so a
        // stale profile section means fgwatch is down — its lastApplied could be arbitrarily old.
        var profiles = _status.GetStatus()?.Profiles;
        if (profiles is null || profiles.Stale) return null;
        var lastApplied = profiles.LastAppliedProfile?.Trim();
        if (string.IsNullOrEmpty(lastApplied)) return null;

        // Only act when the active profile CHANGES — re-asserting the same profile is a no-op.
        if (string.Equals(lastApplied, _lastSeenProfile, StringComparison.OrdinalIgnoreCase)) return null;

        var firstObservation = _lastSeenProfile is null;
        _lastSeenProfile = lastApplied;

        var link = (_commands.GetProfiles()
            .FirstOrDefault(p => string.Equals(p.Name, lastApplied, StringComparison.OrdinalIgnoreCase))
            ?.Optimizer ?? "").Trim();
        var preset = link.Length == 0
            ? null
            : _profiles.BuiltInPresets.Concat(_profiles.Snapshots)
                .FirstOrDefault(p => string.Equals(p.Id, link, StringComparison.OrdinalIgnoreCase));

        if (firstObservation)
        {
            // Seed only — follow SWITCHES, don't re-apply on every launch/enable. (Matches the design:
            // the preset follows a profile CHANGE, and the active profile's preset was already applied
            // when it was last switched to.)
            _lastAppliedPreset = preset?.Id;
            return null;
        }

        if (link.Length == 0) { _lastAppliedPreset = null; return null; } // profile has no preset-link

        if (preset is null)
        {
            EngineLog.Write($"[FancontrolFollower] profile '{lastApplied}' links unknown preset '{link}' — skipped");
            _lastAppliedPreset = null;
            return null;
        }

        // idempotent: the same preset is already in effect → no-op (e.g. two profiles share a preset).
        if (string.Equals(preset.Id, _lastAppliedPreset, StringComparison.OrdinalIgnoreCase)) return null;

        var result = await _profiles.ApplyPresetDetailedAsync(preset.Id, includeDestructive: false);
        _lastAppliedPreset = preset.Id;
        EngineLog.Write($"[FancontrolFollower] profile '{lastApplied}' → preset '{preset.Name}': {result.Summary}");
        return preset.Id;
    }
}
