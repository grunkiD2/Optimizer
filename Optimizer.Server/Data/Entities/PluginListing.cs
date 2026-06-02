namespace Optimizer.Server.Data.Entities;

public class PluginListing
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? AuthorId { get; set; }
    public User? Author { get; set; }

    public string PluginId { get; set; } = "";           // the manifest id, unique
    public string Name { get; set; } = "";
    public string AuthorDisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public string ManifestYaml { get; set; } = "";        // the full manifest content (YAML)
    public string ManifestSha256 { get; set; } = "";      // hex-encoded SHA-256 of ManifestYaml
    public string? Signature { get; set; }                // Ed25519 signature (base64) — set when approved+signed

    public int Downloads { get; set; }
    public double AverageRating { get; set; }
    public int RatingCount { get; set; }
    public bool Verified { get; set; }                    // signed by Optimizer team
    public ListingStatus Status { get; set; } = ListingStatus.Pending;
    public string? RejectionReason { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
