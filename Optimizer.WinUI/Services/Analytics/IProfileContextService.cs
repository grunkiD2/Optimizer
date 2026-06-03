namespace Optimizer.WinUI.Services.Analytics;

/// <summary>
/// Tracks which profiles work well in which contexts. A profile "succeeds" in a
/// context if the user does not switch away from it within a short settling window.
/// </summary>
public interface IProfileContextService
{
    /// <summary>Record that a profile was applied in a context. Returns the pending application id.</summary>
    Task<long> RecordApplicationAsync(string profileId, string context);

    /// <summary>
    /// Resolve any pending applications older than the settling window: those still
    /// "current" count as successes; those superseded by a later apply count as failures.
    /// </summary>
    Task ResolvePendingAsync(TimeSpan settlingWindow);

    /// <summary>Get the association stats for a profile in a context (or null if none).</summary>
    Task<ProfileContextAssociation?> GetAssociationAsync(string profileId, string context);

    /// <summary>Rank profiles by historical success in a context.</summary>
    Task<List<ProfileContextAssociation>> GetBestProfilesForContextAsync(string context, int count = 5);
}

/// <summary>Aggregated success record of a profile within a context.</summary>
public class ProfileContextAssociation
{
    public string ProfileId { get; set; } = "";
    public string Context { get; set; } = "";
    public int ApplyCount { get; set; }
    public int SuccessCount { get; set; }
    public double SuccessRate => ApplyCount == 0 ? 0 : SuccessCount / (double)ApplyCount;
    public DateTime? LastAppliedUtc { get; set; }
}
