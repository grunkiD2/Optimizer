using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

/// <summary>Where the authoritative context answer came from.</summary>
public enum ContextSource { Federation, Guess }

/// <summary>
/// R4: the single source of truth for "what is the machine doing right now".
/// Federation-first: when the Fancontrol system's measured state is configured and fresh,
/// IT owns the answer (fgwatch's applied profile + the brain's measured GAME flag); the
/// process-list guess is only a fallback. Live-audited failure this fixes: the guesser said
/// "Gaming" (steam.exe etc. merely running) while the brain measured IDLE and the profile was
/// AAA-HDR-less desktop — every baseline learner then mislabeled its rows daily.
/// </summary>
public interface IContextAuthority : IContextDetectionService
{
    /// <summary>
    /// True when the federation currently owns context truth (configured + a fresh brain or
    /// fgwatch section). Recomputed on every read — the hard gate in ContextAutomationService
    /// must never act on a stale answer.
    /// </summary>
    bool FederationOwnsContext { get; }

    /// <summary>Where the most recent <see cref="IContextDetectionService.DetectContextAsync"/> answer came from.</summary>
    ContextSource LastSource { get; }
}

public class ContextAuthorityService(
    IFancontrolStatusService fancontrol,
    IContextGuesser guesser) : IContextAuthority
{
    /// <summary>
    /// Fancontrol profiles that mean "the user is gaming". BlueStacks is deliberately NOT here:
    /// it is the farm app (Fancontrol's DOWNGRADE_OK philosophy) — farming must not pollute
    /// Gaming baselines.
    /// </summary>
    private static readonly HashSet<string> GamingProfiles =
        new(StringComparer.OrdinalIgnoreCase) { "AAA-HDR", "AAA-SDR", "Competitive", "Benchmark" };

    public UserIntent UserIntent => guesser.UserIntent;

    public bool FederationOwnsContext => ReadFederationTruth() is not null;

    public ContextSource LastSource { get; private set; } = ContextSource.Guess;

    public async Task<string> DetectContextAsync()
    {
        var truth = ReadFederationTruth();
        if (truth is null)
        {
            // Federation unconfigured or stale — the guess is all we have (standalone-app mode).
            LastSource = ContextSource.Guess;
            return await guesser.GuessContextAsync(suppressGaming: false);
        }

        LastSource = ContextSource.Federation;
        var (brain, profile) = truth.Value;

        // Gaming determination. The lingering-profile trap (live-found during R4 itself):
        // a MANUALLY chosen AAA-HDR sticks as lastApplied for hours after the game exits
        // (manual profiles are deliberately never auto-reverted), so the profile alone must
        // not label an idle night as Gaming — the mirror image of the audited guess bug.
        //   brain fresh:  game flag (watt-measured GAME Schmitt) OR a gaming profile WHILE
        //                 the machine actually runs a mapped app (mode APP/GAME — sub-150 W
        //                 games like yesterday's Destiny run as APP with game=false).
        //   brain stale:  the fresh fgwatch profile alone (degraded but rare).
        var gaming = brain is not null
            ? brain.Game || (IsGamingProfile(profile) && brain.Mode is "GAME" or "APP")
            : IsGamingProfile(profile);
        if (gaming) return "Gaming";

        // The machine measurably is NOT gaming. Running processes may still distinguish
        // Plex/Work, but the guess must never reintroduce Gaming here — suppressing it
        // (instead of coercing afterwards) lets Plex/Work matches surface past an
        // always-running steam.exe/discord.exe.
        return await guesser.GuessContextAsync(suppressGaming: true);
    }

    private static bool IsGamingProfile(string? profile)
        => profile is not null && GamingProfiles.Contains(profile);

    /// <summary>(fresh brain section or null, fresh lastAppliedProfile or null); null when the federation is unconfigured or fully stale.</summary>
    private (FancontrolBrainStatus? Brain, string? Profile)? ReadFederationTruth()
    {
        try
        {
            if (!fancontrol.IsConfigured) return null;
            var st = fancontrol.GetStatus();
            var brain = st?.Brain is { Stale: false } b ? b : null;
            var prof = st?.Profiles is { Stale: false } p ? p : null;
            if (brain is null && prof is null) return null;
            return (brain, prof?.LastAppliedProfile);
        }
        catch { return null; }
    }
}
