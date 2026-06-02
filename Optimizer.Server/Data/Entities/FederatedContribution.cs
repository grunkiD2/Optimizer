namespace Optimizer.Server.Data.Entities;

/// <summary>
/// Stores one user's latest differentially-private model contribution per category.
/// The client applies Laplace noise BEFORE submitting, so the server only ever sees
/// anonymized aggregates — never raw preference data.
/// One row per (UserId, Category) — upserted on each submission.
/// </summary>
public class FederatedContribution
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The contributing user. Used only for upsert keying; never exposed via baselines.</summary>
    public Guid UserId { get; set; }

    /// <summary>Recommendation category (e.g. "Performance", "Storage", "Privacy", "Hardware").</summary>
    public string Category { get; set; } = "";

    /// <summary>DP-noised acceptance rate in [0,1]. The client must apply noise before upload.</summary>
    public double AcceptanceRate { get; set; }

    /// <summary>DP-noised interaction count (used as weight in federated averaging). Always ≥ 0.</summary>
    public int SampleWeight { get; set; }

    public DateTime SubmittedUtc { get; set; } = DateTime.UtcNow;
}
