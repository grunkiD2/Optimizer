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

    [ObservableProperty] private int enabledCount;
    [ObservableProperty] private int totalCount;

    public ObservableCollection<StartupEntry> Entries { get; } = [];

    public string CategoryName => "Startup";
    public string CategoryIcon => "🚀";

    // ICategoryViewModel: alias EnabledCount as ActiveCount
    int ICategoryViewModel.ActiveCount => EnabledCount;

    public StartupCategoryViewModel(IStartupService startupService, IElevationService elevationService)
    {
        _startupService = startupService;
        _elevationService = elevationService;
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

    [RelayCommand]
    public void ToggleEntry(StartupEntry entry)
    {
        _startupService.SetEnabled(entry, !entry.Enabled);
        Load();
    }
}
