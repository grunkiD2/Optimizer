namespace Optimizer.Server.Data.Entities;

public class SyncItem
{
    public long Id { get; set; }  // auto-increment primary key
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public string ItemType { get; set; } = "";  // profile/snapshot/history/settings
    public string ItemId { get; set; } = "";    // app-defined id
    public long Version { get; set; }            // per-user monotonic
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public string Payload { get; set; } = "{}";  // JSON
    public bool IsDeleted { get; set; }
}
