namespace Optimizer.WinUI.Services.Analytics;

/// <summary>
/// The safety gate for unattended optimization. Implements "confirm-on-first-occurrence":
/// an optimization may only auto-apply in a context once it has succeeded there at least
/// N times (configurable). Honors the master kill switch, the feature toggle, and the
/// user's exclusion list.
/// </summary>
public interface IAutoApplyPolicy
{
    /// <summary>Record the outcome of applying an optimization in a context.</summary>
    Task RecordOutcomeAsync(string optimizationId, string context, bool success);

    /// <summary>
    /// Decide whether <paramref name="optimizationId"/> may be auto-applied in
    /// <paramref name="context"/> right now, given settings and learned history.
    /// </summary>
    Task<bool> CanAutoApplyAsync(string optimizationId, string context);

    /// <summary>How many times this optimization has succeeded in this context.</summary>
    Task<int> GetSuccessCountAsync(string optimizationId, string context);
}
