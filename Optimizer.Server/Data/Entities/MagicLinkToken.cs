namespace Optimizer.Server.Data.Entities;

public class MagicLinkToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = "";  // can be for non-yet-existing users
    public string TokenHash { get; set; } = "";  // never store raw token
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
    public string IpAddress { get; set; } = "";
}
