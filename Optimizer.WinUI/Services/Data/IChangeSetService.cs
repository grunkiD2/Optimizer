namespace Optimizer.WinUI.Services.Data;

/// <summary>
/// Records granular change sets (one per optimization) with concrete before/after
/// registry snapshots, and supports selective restore of a single change — even when
/// it was applied as part of a larger profile (linked via <see cref="ChangeSet.GroupId"/>).
/// </summary>
public interface IChangeSetService
{
    /// <summary>
    /// Capture the "before" state of the given registry targets, returning a token the
    /// caller passes back to <see cref="CommitAsync"/> after the optimization runs.
    /// </summary>
    string CaptureBefore(IEnumerable<(string root, string subKey, string valueName)> targets);

    /// <summary>
    /// Persist a completed change: re-captures the "after" state of the same targets and
    /// stores the record. <paramref name="groupId"/> links optimizations applied together
    /// (e.g. a profile). Returns the new change-set id.
    /// </summary>
    Task<long> CommitAsync(
        string optimizationId,
        string title,
        string beforeSnapshot,
        IEnumerable<(string root, string subKey, string valueName)> targets,
        string? groupId = null,
        string? context = null);

    /// <summary>Get recent change sets, newest first.</summary>
    Task<List<ChangeSet>> GetRecentAsync(int count = 100);

    /// <summary>Get the change sets belonging to a group (e.g. a profile application).</summary>
    Task<List<ChangeSet>> GetByGroupAsync(string groupId);

    /// <summary>Restore a single change set's "before" snapshot and mark it undone.</summary>
    Task<bool> RestoreAsync(long changeSetId);
}

/// <summary>A recorded, reversible change with before/after state.</summary>
public class ChangeSet
{
    public long Id { get; set; }
    public string OptimizationId { get; set; } = "";
    public string Title { get; set; } = "";
    public string? GroupId { get; set; }
    public string? BeforeState { get; set; }
    public string? AfterState { get; set; }
    public DateTime AppliedAtUtc { get; set; }
    public bool Reversible { get; set; }
    public bool IsUndone { get; set; }
    public string? Context { get; set; }
}
