using System.Text.Json;
using Optimizer.WinUI.Helpers;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services.Cloud;

namespace Optimizer.WinUI.Services;

public class MarketplaceService : IMarketplaceService
{
    private readonly IWindowsOptimizerService _optimizer;
    protected readonly IOptimizerCloudClient _cloud;

    private static readonly string RatingsFile = AppPaths.GetDataFile("marketplace-ratings.json");

    private Dictionary<string, int> _ratings = new();

    public MarketplaceService(IWindowsOptimizerService optimizer, IOptimizerCloudClient cloud)
    {
        _optimizer = optimizer;
        _cloud = cloud;
        LoadRatings();
    }

    public virtual async Task<IReadOnlyList<MarketplaceEntry>> LoadCatalogAsync(bool includeRemote = false)
    {
        var local = await LoadBundledCatalogAsync();
        if (!includeRemote || !_cloud.IsAuthenticated) return local;

        try
        {
            var remote = await _cloud.BrowseMarketplaceAsync(null, null, "downloads", 1, 100);
            if (remote == null) return local;

            var remoteEntries = remote.Listings.Select(r => new MarketplaceEntry
            {
                Id = r.PublicId,
                Name = r.Name,
                Author = r.AuthorDisplayName,
                Description = r.Description,
                Category = r.Category,
                Tags = r.Tags.ToList(),
                Optimizations = r.Optimizations.ToList(),
                Downloads = r.Downloads,
                AverageRating = r.AverageRating,
                RatingCount = r.RatingCount,
                Verified = r.Verified,
                Source = r.Featured ? "Featured" : "Community"
            }).ToList();

            // Apply local ratings to remote entries
            foreach (var entry in remoteEntries)
                if (_ratings.TryGetValue(entry.Id, out var rating))
                    entry.UserRating = rating;

            // Merge: remote entries take precedence for same PublicId
            var localIds = new HashSet<string>(local.Select(l => l.Id));
            var merged = local.Concat(remoteEntries.Where(r => !localIds.Contains(r.Id))).ToList();
            return merged;
        }
        catch (Exception ex)
        {
            EngineLog.Error("Marketplace remote merge failed", ex);
            return local;
        }
    }

    private async Task<List<MarketplaceEntry>> LoadBundledCatalogAsync()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "marketplace-catalog.json");
            if (!File.Exists(path)) return [];

            var json = await File.ReadAllTextAsync(path);
            using var doc = JsonDocument.Parse(json);
            var entries = new List<MarketplaceEntry>();

            if (doc.RootElement.TryGetProperty("entries", out var entriesEl))
            {
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                foreach (var el in entriesEl.EnumerateArray())
                {
                    var entry = JsonSerializer.Deserialize<MarketplaceEntry>(el.GetRawText(), opts);
                    if (entry is null) continue;

                    entry.Source = "Bundled";

                    if (_ratings.TryGetValue(entry.Id, out var r))
                        entry.UserRating = r;

                    entries.Add(entry);
                }
            }

            return entries;
        }
        catch (Exception ex)
        {
            EngineLog.Error("Marketplace catalog load failed", ex);
            return [];
        }
    }

    public async Task<bool> InstallAsync(MarketplaceEntry entry)
    {
        try
        {
            foreach (var optId in entry.Optimizations)
                await _optimizer.ApplyOptimizationAsync(optId);

            entry.IsInstalled = true;
            return true;
        }
        catch (Exception ex)
        {
            EngineLog.Error($"Marketplace install failed for {entry.Id}", ex);
            return false;
        }
    }

    public Task RateAsync(string id, int rating)
    {
        _ratings[id] = Math.Clamp(rating, 0, 5);
        SaveRatings();
        return Task.CompletedTask;
    }

    public async Task<string> GenerateSubmissionAsync(MarketplaceEntry entry)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Optimizer Submissions");
        Directory.CreateDirectory(dir);

        var path = Path.Combine(dir, $"submission-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        var opts = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(entry, opts));
        return path;
    }

    public IReadOnlyDictionary<string, int> GetUserRatings() => _ratings;

    // ── Persistence ─────────────────────────────────────────────────────────

    private void LoadRatings()
    {
        try
        {
            if (!File.Exists(RatingsFile)) return;
            var json = File.ReadAllText(RatingsFile);
            _ratings = JsonSerializer.Deserialize<Dictionary<string, int>>(json) ?? new();
        }
        catch { /* ratings are best-effort */ }
    }

    private void SaveRatings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RatingsFile)!);
            File.WriteAllText(RatingsFile, JsonSerializer.Serialize(_ratings));
        }
        catch { }
    }
}
