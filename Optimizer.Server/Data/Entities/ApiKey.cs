namespace Optimizer.Server.Data.Entities;

public class ApiKey
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public string Name { get; set; } = "";          // user-chosen label, e.g. "CI pipeline"
    public string Prefix { get; set; } = "";          // first chars shown in UI, e.g. "opt_live_a1b2"
    public string KeyHash { get; set; } = "";          // SHA-256 of the full key — raw key never stored
    public string ScopesCsv { get; set; } = "";        // comma-separated scopes

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }        // null = no expiry
    public DateTime? RevokedAtUtc { get; set; }

    public bool IsActive => RevokedAtUtc == null && (ExpiresAtUtc == null || ExpiresAtUtc > DateTime.UtcNow);
}
