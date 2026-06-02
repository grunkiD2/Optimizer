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
    public DbSet<MarketplaceListing> MarketplaceListings => Set<MarketplaceListing>();
    public DbSet<MarketplaceRating> MarketplaceRatings => Set<MarketplaceRating>();
    public DbSet<MarketplaceReport> MarketplaceReports => Set<MarketplaceReport>();
    public DbSet<PluginListing> PluginListings => Set<PluginListing>();
    public DbSet<PluginRating> PluginRatings => Set<PluginRating>();

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

        mb.Entity<MarketplaceListing>(e =>
        {
            e.HasIndex(l => l.PublicId).IsUnique();
            e.HasIndex(l => new { l.Status, l.Featured });
            e.HasIndex(l => l.Category);
            e.Property(l => l.PublicId).IsRequired().HasMaxLength(128);
            e.Property(l => l.Name).IsRequired().HasMaxLength(80);
            e.Property(l => l.Category).HasMaxLength(64);
        });

        mb.Entity<MarketplaceRating>(e =>
        {
            e.HasIndex(r => new { r.ListingId, r.UserId }).IsUnique();
        });

        mb.Entity<MarketplaceReport>(e =>
        {
            e.HasIndex(r => r.ListingId);
        });

        mb.Entity<PluginListing>(e =>
        {
            e.HasIndex(l => l.PluginId).IsUnique();
            e.HasIndex(l => new { l.Status, l.Verified });
            e.Property(l => l.PluginId).IsRequired().HasMaxLength(128);
            e.Property(l => l.Name).IsRequired().HasMaxLength(80);
            e.Property(l => l.Category).HasMaxLength(64);
            e.Property(l => l.ManifestSha256).HasMaxLength(64);
        });

        mb.Entity<PluginRating>(e =>
        {
            e.HasIndex(r => new { r.ListingId, r.UserId }).IsUnique();
        });
    }
}
