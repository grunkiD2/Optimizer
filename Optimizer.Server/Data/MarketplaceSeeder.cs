using Microsoft.EntityFrameworkCore;
using Optimizer.Server.Data.Entities;
using System.Text.Json;

namespace Optimizer.Server.Data;

public static class MarketplaceSeeder
{
    public static async Task SeedAsync(OptimizerDbContext db, ILogger? logger = null)
    {
        if (await db.MarketplaceListings.AnyAsync()) return;  // already seeded

        var entries = new[]
        {
            new MarketplaceListing
            {
                PublicId = "mkt-gaming-ultra",
                Name = "Gaming Ultra (community)",
                AuthorDisplayName = "Optimizer Team",
                Description = "Maximum performance for esports. Disables all background apps, animations, telemetry, and forces high-performance power plan.",
                Category = "Gaming",
                TagsJson = JsonSerializer.Serialize(new[] { "fps", "low-latency", "esports" }),
                OptimizationsJson = JsonSerializer.Serialize(new[] { "DisableBackgroundApps", "DisableAnimations", "DisableVisualEffects", "OptimizePowerSettings", "OptimizeNetworkSettings", "DisableTelemetry" }),
                Downloads = 12450,
                AverageRating = 4.7,
                RatingCount = 213,
                Verified = true,
                Featured = true,
                Status = ListingStatus.Approved
            },
            new MarketplaceListing
            {
                PublicId = "mkt-developer-focus",
                Name = "Developer Focus",
                AuthorDisplayName = "Optimizer Team",
                Description = "Reduces distractions and frees memory for IDEs and compilers. Keeps notifications minimal.",
                Category = "Productivity",
                TagsJson = JsonSerializer.Serialize(new[] { "coding", "focus", "performance" }),
                OptimizationsJson = JsonSerializer.Serialize(new[] { "DisableBackgroundApps", "DisableAnimations", "DisableConsumerFeatures", "OptimizePowerSettings" }),
                Downloads = 8920,
                AverageRating = 4.5,
                RatingCount = 167,
                Verified = true,
                Featured = false,
                Status = ListingStatus.Approved
            },
            new MarketplaceListing
            {
                PublicId = "mkt-streamer",
                Name = "Streamer Setup",
                AuthorDisplayName = "Optimizer Team",
                Description = "Optimized for OBS/XSplit streaming. Balances CPU available for game + encoder.",
                Category = "Content Creation",
                TagsJson = JsonSerializer.Serialize(new[] { "obs", "streaming", "twitch" }),
                OptimizationsJson = JsonSerializer.Serialize(new[] { "DisableBackgroundApps", "OptimizeNetworkSettings", "DisableAnimations" }),
                Downloads = 6234,
                AverageRating = 4.4,
                RatingCount = 98,
                Verified = true,
                Featured = false,
                Status = ListingStatus.Approved
            },
            new MarketplaceListing
            {
                PublicId = "mkt-laptop-battery",
                Name = "Laptop Battery Saver+",
                AuthorDisplayName = "Optimizer Team",
                Description = "Aggressive battery extension for laptops. Reduces brightness commands and disables non-essential services.",
                Category = "Laptop",
                TagsJson = JsonSerializer.Serialize(new[] { "battery", "mobile", "travel" }),
                OptimizationsJson = JsonSerializer.Serialize(new[] { "DisableBackgroundApps", "OptimizePowerSettings", "DisableTelemetry" }),
                Downloads = 9810,
                AverageRating = 4.3,
                RatingCount = 184,
                Verified = true,
                Featured = false,
                Status = ListingStatus.Approved
            },
            new MarketplaceListing
            {
                PublicId = "mkt-privacy-paranoid",
                Name = "Privacy Paranoid",
                AuthorDisplayName = "Optimizer Team",
                Description = "Maximum privacy. Disables every telemetry, AI feature, and tracking option available.",
                Category = "Privacy",
                TagsJson = JsonSerializer.Serialize(new[] { "privacy", "telemetry", "tracking" }),
                OptimizationsJson = JsonSerializer.Serialize(new[] { "DisableTelemetry", "DisableConsumerFeatures" }),
                Downloads = 14250,
                AverageRating = 4.8,
                RatingCount = 312,
                Verified = true,
                Featured = true,
                Status = ListingStatus.Approved
            },
            new MarketplaceListing
            {
                PublicId = "mkt-quiet-office",
                Name = "Quiet Office",
                AuthorDisplayName = "Optimizer Team",
                Description = "Lower power for reduced fan noise and heat. Ideal for shared workspaces.",
                Category = "Productivity",
                TagsJson = JsonSerializer.Serialize(new[] { "quiet", "office", "thermal" }),
                OptimizationsJson = JsonSerializer.Serialize(new[] { "OptimizePowerSettings", "DisableBackgroundApps" }),
                Downloads = 4120,
                AverageRating = 4.2,
                RatingCount = 76,
                Verified = true,
                Featured = false,
                Status = ListingStatus.Approved
            },
            new MarketplaceListing
            {
                PublicId = "mkt-emergency-cleanup",
                Name = "Emergency Cleanup",
                AuthorDisplayName = "Optimizer Team",
                Description = "When disk is critically full. Clears temp files, Windows Update cache, and old logs.",
                Category = "Maintenance",
                TagsJson = JsonSerializer.Serialize(new[] { "cleanup", "diskspace", "emergency" }),
                OptimizationsJson = JsonSerializer.Serialize(new[] { "ClearTemporaryFiles", "ClearWindowsUpdateCache" }),
                Downloads = 7890,
                AverageRating = 4.6,
                RatingCount = 145,
                Verified = true,
                Featured = false,
                Status = ListingStatus.Approved
            },
        };

        db.MarketplaceListings.AddRange(entries);
        await db.SaveChangesAsync();
        logger?.LogInformation("Marketplace seeded with {Count} listings", entries.Length);
    }
}
