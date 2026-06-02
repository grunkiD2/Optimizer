namespace Optimizer.Server.Data.Entities;

public enum ListingStatus { Pending, Approved, Rejected }

public class MarketplaceListing
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? AuthorId { get; set; }  // null for seed/system listings
    public User? Author { get; set; }

    // Public fields
    public string PublicId { get; set; } = "";  // stable id used by clients (e.g. "mkt-gaming-ultra")
    public string Name { get; set; } = "";
    public string AuthorDisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public string TagsJson { get; set; } = "[]";  // serialized list<string>
    public string OptimizationsJson { get; set; } = "[]";  // list of optimization IDs

    // Counters (denormalized for performance)
    public int Downloads { get; set; }
    public double AverageRating { get; set; }
    public int RatingCount { get; set; }

    // Flags
    public bool Verified { get; set; }  // Optimizer team
    public bool Featured { get; set; }  // homepage promotion
    public ListingStatus Status { get; set; } = ListingStatus.Pending;
    public string? RejectionReason { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
