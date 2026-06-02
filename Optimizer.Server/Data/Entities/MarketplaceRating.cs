namespace Optimizer.Server.Data.Entities;

public class MarketplaceRating
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ListingId { get; set; }
    public MarketplaceListing? Listing { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public int Stars { get; set; }  // 1-5
    public string? Comment { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
