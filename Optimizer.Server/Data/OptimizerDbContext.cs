using Microsoft.EntityFrameworkCore;
using Optimizer.Server.Data.Entities;

namespace Optimizer.Server.Data;

public class OptimizerDbContext : DbContext
{
    public OptimizerDbContext(DbContextOptions<OptimizerDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<MagicLinkToken> MagicLinkTokens => Set<MagicLinkToken>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<SyncItem> SyncItems => Set<SyncItem>();
    public DbSet<UserVersionCounter> UserVersionCounters => Set<UserVersionCounter>();

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

        mb.Entity<SyncItem>(e =>
        {
            e.HasIndex(s => new { s.UserId, s.ItemType, s.ItemId }).IsUnique();
            e.HasIndex(s => new { s.UserId, s.Version });
            e.Property(s => s.ItemType).IsRequired().HasMaxLength(32);
            e.Property(s => s.ItemId).IsRequired().HasMaxLength(128);
            e.Property(s => s.Payload).IsRequired();
        });

        mb.Entity<UserVersionCounter>(e =>
        {
            e.HasKey(c => c.UserId);
            e.HasOne(c => c.User).WithOne().HasForeignKey<UserVersionCounter>(c => c.UserId);
        });
    }
}
