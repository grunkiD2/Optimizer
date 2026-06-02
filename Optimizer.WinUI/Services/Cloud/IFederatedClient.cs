using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services.Cloud;

/// <summary>
/// Orchestrates the opt-in federated-learning scaffold.
///
/// When FederatedLearningEnabled = true AND the user is authenticated:
///   1. Computes the local privatized summary (Laplace DP noise applied locally).
///   2. Uploads only the DP-noised aggregates to the server — NEVER raw preferences.
///   3. Fetches community baselines that can be used as soft signals in recommendations.
///
/// When disabled (default): no data leaves the device — period.
/// This is a lightweight scaffold, not a production federated-learning system.
/// </summary>
public interface IFederatedClient
{
    /// <summary>
    /// If federated learning is enabled and the user is authenticated, uploads
    /// a differentially-private summary and fetches community baselines.
    /// Safe to call on app start or once per day; no-ops when disabled or not authenticated.
    /// </summary>
    Task SyncAsync();

    /// <summary>
    /// The latest community baselines fetched from the server, or null/empty if unavailable.
    /// These are soft signals — use them to gently inform recommendation ordering,
    /// clearly labelled as community-derived.
    /// </summary>
    IReadOnlyList<FederatedCommunityBaseline> CommunityBaselines { get; }
}
