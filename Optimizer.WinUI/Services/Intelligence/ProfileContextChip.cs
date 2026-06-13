// ProfileContextChip.cs
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services.Intelligence;

public enum ChipKind { Unknown, AppBound, Mood }

/// <summary>The persistent state-chip atop the editor (Profil 2.0 §1): is the live context
/// app-bound (fgwatch auto-applied a profile because the foreground exe is mapped) or a manual mood?
/// Pure derivation from the federation status + the mapped-exe set; one-click convert is wired in UI.</summary>
public sealed record ContextChip(ChipKind Kind, string? AppExe, string? ProfileName, bool Stale);

public static class ProfileContextChip
{
    /// <param name="status">Latest IFancontrolStatusService.GetStatus(), or null if unavailable.</param>
    /// <param name="mappedExes">Foreground exes that fgwatch maps to a profile (from GetMappedPrograms()).</param>
    public static ContextChip Derive(FancontrolStatus? status, IReadOnlySet<string> mappedExes)
    {
        if (status?.Profiles is not { } prof)
            return new ContextChip(ChipKind.Unknown, null, null, status?.Brain?.Stale ?? false);

        bool stale = prof.Stale || (status.Brain?.Stale ?? false);
        var fgExe = prof.ForegroundExe;
        bool appBound = prof.Enabled
            && !string.IsNullOrEmpty(fgExe)
            && mappedExes.Contains(fgExe);

        return appBound
            ? new ContextChip(ChipKind.AppBound, fgExe, prof.LastAppliedProfile, stale)
            : new ContextChip(ChipKind.Mood, null, prof.LastAppliedProfile ?? status.Brain?.Mode, stale);
    }
}
