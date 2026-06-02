using System.Text.Json;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public class RecommendationsService : IRecommendationsService
{
    private readonly IDiagnosticsService _diagnostics;
    private readonly IWindowsOptimizerService _optimizer;
    private readonly HashSet<string> _dismissedIds = [];
    private readonly string _dismissedFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Optimizer", "dismissed-recommendations.json");

    public RecommendationsService(IDiagnosticsService diagnostics, IWindowsOptimizerService optimizer)
    {
        _diagnostics = diagnostics;
        _optimizer = optimizer;
        LoadDismissed();
    }

    public async Task<IReadOnlyList<Recommendation>> GenerateAsync()
    {
        var recs = new List<Recommendation>();

        // Convert diagnostic findings to recommendations
        try
        {
            var findings = await _diagnostics.RunFullScanAsync();
            foreach (var f in findings.Where(f => !_dismissedIds.Contains(f.Id)))
            {
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
        if (!_dismissedIds.Contains("suggest-privacy-preset"))
        {
            try
            {
                var profiles = _optimizer.GetBuiltInPresets();
                if (profiles.Any(p => p.Id == "preset-privacy"))
                {
                    recs.Add(new Recommendation
                    {
                        Id = "suggest-privacy-preset",
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

        return recs;
    }

    public Task DismissAsync(string id)
    {
        _dismissedIds.Add(id);
        SaveDismissed();
        return Task.CompletedTask;
    }

    public Task ResetDismissedAsync()
    {
        _dismissedIds.Clear();
        SaveDismissed();
        return Task.CompletedTask;
    }

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
}
