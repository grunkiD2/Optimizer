using System.Text.Json;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public class MarketplaceService : IMarketplaceService
{
    private readonly IWindowsOptimizerService _optimizer;

    private static readonly string RatingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Optimizer", "marketplace-ratings.json");

    private Dictionary<string, int> _ratings = new();

    public MarketplaceService(IWindowsOptimizerService optimizer)
    {
        _optimizer = optimizer;
        LoadRatings();
    }

    public async Task<IReadOnlyList<MarketplaceEntry>> LoadCatalogAsync()
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
