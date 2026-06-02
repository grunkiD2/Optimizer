using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;

namespace Optimizer.WinUI.ViewModels;

public partial class StartupCategoryViewModel : ObservableObject, ICategoryViewModel
{
    private readonly IStartupService _startupService;
    private readonly IElevationService _elevationService;
    private readonly IBootAnalysisService _bootService;

    [ObservableProperty] private int enabledCount;
    [ObservableProperty] private int totalCount;

    // ── Boot metrics ─────────────────────────────────────────────────────────
    public ObservableCollection<BootMetrics> BootHistory { get; } = [];
    [ObservableProperty] private BootMetrics? lastBoot;
    [ObservableProperty] private string averageBootText = "—";

    public ObservableCollection<StartupEntry> Entries { get; } = [];

    public string CategoryName => "Startup";
    public string CategoryIcon => "🚀";

    // ICategoryViewModel: alias EnabledCount as ActiveCount
    int ICategoryViewModel.ActiveCount => EnabledCount;

    public StartupCategoryViewModel(
        IStartupService startupService,
        IElevationService elevationService,
        IBootAnalysisService bootService)
    {
        _startupService = startupService;
        _elevationService = elevationService;
        _bootService = bootService;
    }

    public void Load()
    {
        Entries.Clear();
        var entries = _startupService.GetEntries();
        foreach (var entry in entries)
            Entries.Add(entry);

        TotalCount = Entries.Count;
        EnabledCount = Entries.Count(e => e.Enabled);
    }

    public async Task LoadBootMetricsAsync()
    {
        var history = await _bootService.GetBootHistoryAsync(10);
        BootHistory.Clear();
        foreach (var b in history) BootHistory.Add(b);
        LastBoot = history.FirstOrDefault();

        if (history.Count > 0)
        {
            // Only average entries that have a meaningful duration (>0)
            var meaningful = history.Where(b => b.BootDuration.TotalSeconds > 0).ToList();
            AverageBootText = meaningful.Count > 0
                ? $"{meaningful.Average(b => b.BootDuration.TotalSeconds):F1}s"
                : "—";
        }
        else
        {
            AverageBootText = "—";
        }
    }

    [RelayCommand]
    public void ToggleEntry(StartupEntry entry)
    {
        _startupService.SetEnabled(entry, !entry.Enabled);
        Load();
    }
}
