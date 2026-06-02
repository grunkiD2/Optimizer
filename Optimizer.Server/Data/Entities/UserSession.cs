namespace Optimizer.Server.Data.Entities;

public class UserSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public string RefreshTokenHash { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public string DeviceName { get; set; } = "";  // optional, like "Sam's WinUI Desktop"
    public string IpAddress { get; set; } = "";
}
