using System.Text.Json;
using Optimizer.WinUI.Helpers;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public class RecommendationsService : IRecommendationsService
{
    private readonly IDiagnosticsService _diagnostics;
    private readonly IWindowsOptimizerService _optimizer;

    private readonly HashSet<string> _dismissedIds = [];

    // Personalization
    private readonly Dictionary<string, RecommendationPreference> _preferences = new(StringComparer.OrdinalIgnoreCase);

    private readonly string _dismissedFile = AppPaths.GetDataFile("dismissed-recommendations.json");

    private readonly string _prefsFile = AppPaths.GetDataFile("rec-preferences.json");

    public RecommendationsService(IDiagnosticsService diagnostics, IWindowsOptimizerService optimizer)
    {
        _diagnostics = diagnostics;
        _optimizer = optimizer;
        LoadDismissed();
        LoadPreferences();
    }

    public async Task<IReadOnlyList<Recommendation>> GenerateAsync()
    {
        var recs = new List<Recommendation>();
        var now = DateTime.UtcNow;

        // Convert diagnostic findings to recommendations
        try
        {
            var findings = await _diagnostics.RunFullScanAsync();
            foreach (var f in findings.Where(f => !_dismissedIds.Contains(f.Id)))
            {
                if (IsSnoozed(f.Id, now)) continue;
                if (IsPermanentlyHidden(f.Id)) continue;

                recs.Add(new Recommendation
                {
                    Id = f.Id,
                    Title = f.Title,
                    Description = $"{f.Description} {f.Recommendation}",
                    Severity = f.Severity,
                    Category = f.Category,
                    ActionLabel = f.Category switch
                    {
                        FindingCategory.Storage => "Clean Up",
                        FindingCategory.Privacy => "Adjust Settings",
                        FindingCategory.Security => "Review",
                        _ => "Fix"
                    }
                });
            }
        }
        catch { }

        // Suggest applying Privacy Maximum preset if user hasn't dismissed it
        const string privacyPresetId = "suggest-privacy-preset";
        if (!_dismissedIds.Contains(privacyPresetId)
            && !IsSnoozed(privacyPresetId, now)
            && !IsPermanentlyHidden(privacyPresetId))
        {
            try
            {
                var profiles = _optimizer.GetBuiltInPresets();
                if (profiles.Any(p => p.Id == "preset-privacy"))
                {
                    recs.Add(new Recommendation
                    {
                        Id = privacyPresetId,
                        Title = "Boost your privacy in one click",
                        Description = "Apply the Privacy Maximum preset to disable telemetry, ads, and tracking features.",
                        Severity = FindingSeverity.Info,
                        Category = FindingCategory.Privacy,
                        ActionLabel = "Apply Preset",
                        QuickAction = async () =>
                        {
                            var result = await _optimizer.ApplyProfileAsync("preset-privacy");
                            return result;
                        }
                    });
                }
            }
            catch { }
        }

        // Apply personalization: sort by accept score descending
        recs.Sort((a, b) =>
        {
            int scoreA = GetPersonalizationScore(a.Id);
            int scoreB = GetPersonalizationScore(b.Id);
            if (scoreB != scoreA) return scoreB.CompareTo(scoreA);
            // Fall back to severity ordering
            return ((int)b.Severity).CompareTo((int)a.Severity);
        });

        return recs;
    }

    public Task DismissAsync(string id)
    {
        _dismissedIds.Add(id);
        SaveDismissed();

        // Record dismiss in preferences
        var pref = GetOrCreatePref(id);
        pref.DismissCount++;
        pref.LastShownUtc = DateTime.UtcNow;
        SavePreferences();

        return Task.CompletedTask;
    }

    public Task ResetDismissedAsync()
    {
        _dismissedIds.Clear();
        SaveDismissed();
        return Task.CompletedTask;
    }

    public Task RecordAcceptedAsync(string id)
    {
        var pref = GetOrCreatePref(id);
        pref.AcceptCount++;
        pref.LastShownUtc = DateTime.UtcNow;
        SavePreferences();
        return Task.CompletedTask;
    }

    public Task SnoozeAsync(string id, TimeSpan duration)
    {
        var pref = GetOrCreatePref(id);
        pref.SnoozedUntilUtc = DateTime.UtcNow.Add(duration);
        pref.LastShownUtc = DateTime.UtcNow;
        SavePreferences();
        return Task.CompletedTask;
    }

    public IReadOnlyDictionary<string, RecommendationPreference> GetPreferences()
        => _preferences;

    // ── Helpers ─────────────────────────────────────────────────────────────

    private bool IsSnoozed(string id, DateTime nowUtc)
    {
        if (_preferences.TryGetValue(id, out var pref) && pref.SnoozedUntilUtc.HasValue)
            return pref.SnoozedUntilUtc.Value > nowUtc;
        return false;
    }

    private bool IsPermanentlyHidden(string id)
    {
        if (_preferences.TryGetValue(id, out var pref))
            return pref.DismissCount >= 3;
        return false;
    }

    private int GetPersonalizationScore(string id)
    {
        if (_preferences.TryGetValue(id, out var pref))
            return pref.AcceptCount - pref.DismissCount;
        return 0;
    }

    private RecommendationPreference GetOrCreatePref(string id)
    {
        if (!_preferences.TryGetValue(id, out var pref))
        {
            pref = new RecommendationPreference { Id = id };
            _preferences[id] = pref;
        }
        return pref;
    }

    // ── Persistence ──────────────────────────────────────────────────────────

    private void LoadDismissed()
    {
        try
        {
            if (File.Exists(_dismissedFile))
            {
                var json = File.ReadAllText(_dismissedFile);
                var list = JsonSerializer.Deserialize<List<string>>(json);
                if (list != null)
                    foreach (var id in list)
                        _dismissedIds.Add(id);
            }
        }
        catch { }
    }

    private void SaveDismissed()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_dismissedFile)!);
            File.WriteAllText(_dismissedFile, JsonSerializer.Serialize(_dismissedIds.ToList()));
        }
        catch { }
    }

    private void LoadPreferences()
    {
        try
        {
            if (File.Exists(_prefsFile))
            {
                var json = File.ReadAllText(_prefsFile);
                var list = JsonSerializer.Deserialize<List<RecommendationPreference>>(json);
                if (list != null)
                    foreach (var p in list)
                        _preferences[p.Id] = p;
            }
        }
        catch { }
    }

    private void SavePreferences()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_prefsFile)!);
            File.WriteAllText(_prefsFile, JsonSerializer.Serialize(_preferences.Values.ToList()));
        }
        catch { }
    }
}
