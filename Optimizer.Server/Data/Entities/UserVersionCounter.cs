namespace Optimizer.Server.Data.Entities;

// Per-user monotonic version counter for sync.
public class UserVersionCounter
{
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public long CurrentVersion { get; set; }
}
