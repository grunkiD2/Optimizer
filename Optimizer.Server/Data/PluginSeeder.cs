using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Optimizer.Server.Data.Entities;
using Optimizer.Server.Services;

namespace Optimizer.Server.Data;

public static class PluginSeeder
{
    public static async Task SeedAsync(OptimizerDbContext db, IPluginSigningService signing, ILogger? logger = null)
    {
        if (await db.PluginListings.AnyAsync()) return;  // already seeded

        var disableCortana = """
            manifest_version: 1
            id: community-disable-cortana
            name: Disable Cortana
            description: Stops Cortana from running in the background and removes it from the taskbar.
            author: Optimizer Community
            category: Privacy
            icon: "\U0001F507"
            requires_admin: true
            reversible: true
            pros:
              - Frees background memory
              - Reduces telemetry
            cons:
              - Loses Cortana voice features
            changes:
              - type: registry
                path: HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Search
                value: AllowCortana
                value_type: dword
                apply: "0"
                revert: delete
            """;

        var disableWebSearch = """
            manifest_version: 1
            id: community-disable-web-search
            name: Disable Web Search in Start
            description: Removes Bing web search results from the Windows Start menu search, keeping results local only.
            author: Optimizer Community
            category: Privacy
            requires_admin: true
            reversible: true
            pros:
              - Faster Start menu search
              - No search queries sent to Bing
              - Reduces bandwidth usage
            cons:
              - No web results in Start search
            changes:
              - type: registry
                path: HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Search
                value: DisableWebSearch
                value_type: dword
                apply: "1"
                revert: delete
              - type: registry
                path: HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Search
                value: BingSearchEnabled
                value_type: dword
                apply: "0"
                revert: "1"
            """;

        var entries = new[]
        {
            CreateEntry("community-disable-cortana", "Disable Cortana",
                "Optimizer Community", "Privacy",
                "Stops Cortana from running in the background and removes it from the taskbar.",
                disableCortana, signing, downloads: 8420, rating: 4.6, ratingCount: 143),

            CreateEntry("community-disable-web-search", "Disable Web Search in Start",
                "Optimizer Community", "Privacy",
                "Removes Bing web search results from the Windows Start menu search, keeping results local only.",
                disableWebSearch, signing, downloads: 11230, rating: 4.8, ratingCount: 219),
        };

        db.PluginListings.AddRange(entries);
        await db.SaveChangesAsync();
        logger?.LogInformation("Plugin marketplace seeded with {Count} listings", entries.Length);
    }

    private static PluginListing CreateEntry(
        string pluginId, string name, string author, string category, string description,
        string manifestYaml, IPluginSigningService signing,
        int downloads, double rating, int ratingCount)
    {
        var sha256    = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(manifestYaml))).ToLowerInvariant();
        // Sign only when the service is configured (private key is available).
        // In test environments where signing is disabled the seeded entries have an empty
        // signature — they will fail client-side signature verification, which is fine for tests.
        var signature = signing.IsConfigured ? signing.Sign(manifestYaml) : string.Empty;

        return new PluginListing
        {
            PluginId          = pluginId,
            Name              = name,
            AuthorDisplayName = author,
            Description       = description,
            Category          = category,
            ManifestYaml      = manifestYaml,
            ManifestSha256    = sha256,
            Signature         = signature,
            Downloads         = downloads,
            AverageRating     = rating,
            RatingCount       = ratingCount,
            Verified          = true,
            Status            = ListingStatus.Approved
        };
    }
}
