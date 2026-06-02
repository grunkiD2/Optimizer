using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;

namespace Optimizer.WinUI.ViewModels;

public partial class EventLogsViewModel : ObservableObject
{
    private readonly IEventLogService _eventService;

    [ObservableProperty] private bool   isLoading;
    [ObservableProperty] private string selectedLog   = "Application";
    [ObservableProperty] private string selectedLevel = "All";
    [ObservableProperty] private string searchText    = "";
    [ObservableProperty] private string statusMessage = "Select a log to view entries.";

    // Selected entry for detail flyout
    [ObservableProperty] private EventLogEntryInfo? selectedEntry;
    [ObservableProperty] private bool isDetailOpen;

    public List<string> LogOptions   { get; } = ["Application", "System", "Security", "Setup"];
    public List<string> LevelOptions { get; } = ["All", "Critical", "Error", "Warning", "Information"];

    public ObservableCollection<EventLogEntryInfo> Entries { get; } = [];

    public bool IsEmpty => !IsLoading && Entries.Count == 0;

    public EventLogsViewModel(IEventLogService eventService)
    {
        _eventService = eventService;
        Entries.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
    }

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsLoading) return;
        IsLoading     = true;
        StatusMessage = $"Loading {SelectedLog} log…";
        try
        {
            var entries  = await _eventService.GetEntriesAsync(
                SelectedLog,
                200,
                SelectedLevel == "All" ? null : SelectedLevel);

            var filtered = string.IsNullOrWhiteSpace(SearchText)
                ? entries
                : entries.Where(e =>
                    e.Message.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                    e.Source.Contains(SearchText,  StringComparison.OrdinalIgnoreCase));

            Entries.Clear();
            foreach (var e in filtered)
                Entries.Add(e);

            StatusMessage = $"{Entries.Count} entr{(Entries.Count == 1 ? "y" : "ies")} — {SelectedLog}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void ShowDetail(EventLogEntryInfo entry)
    {
        SelectedEntry = entry;
        IsDetailOpen  = true;
    }

    public void CloseDetail() => IsDetailOpen = false;

    // Auto-reload when filter dropdowns change
    partial void OnSelectedLogChanged(string value)   => _ = LoadAsync();
    partial void OnSelectedLevelChanged(string value) => _ = LoadAsync();
}
