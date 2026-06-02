using Microsoft.EntityFrameworkCore;
using Optimizer.Server.Data.Entities;

namespace Optimizer.Server.Data;

public class OptimizerDbContext : DbContext
{
    public OptimizerDbContext(DbContextOptions<OptimizerDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<MagicLinkToken> MagicLinkTokens => Set<MagicLinkToken>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<User>(e =>
        {
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.Email).IsRequired().HasMaxLength(320);
            e.Property(u => u.DisplayName).HasMaxLength(100);
        });

        mb.Entity<MagicLinkToken>(e =>
        {
            e.HasIndex(t => t.TokenHash).IsUnique();
            e.HasIndex(t => t.Email);
            e.Property(t => t.Email).IsRequired().HasMaxLength(320);
            e.Property(t => t.TokenHash).IsRequired().HasMaxLength(128);
        });

        mb.Entity<UserSession>(e =>
        {
            e.HasIndex(s => s.RefreshTokenHash).IsUnique();
            e.HasOne(s => s.User).WithMany(u => u.Sessions).HasForeignKey(s => s.UserId);
        });
    }
}
