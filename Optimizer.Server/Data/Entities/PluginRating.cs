namespace Optimizer.Server.Data.Entities;

public class PluginRating
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ListingId { get; set; }
    public PluginListing? Listing { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public int Stars { get; set; }  // 1-5
    public string? Comment { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
