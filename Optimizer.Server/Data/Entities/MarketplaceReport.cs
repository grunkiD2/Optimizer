namespace Optimizer.Server.Data.Entities;

public enum ReportReason { Spam, Inappropriate, Malicious, Other }

public class MarketplaceReport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ListingId { get; set; }
    public Guid ReporterUserId { get; set; }
    public ReportReason Reason { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public bool Resolved { get; set; }
}
