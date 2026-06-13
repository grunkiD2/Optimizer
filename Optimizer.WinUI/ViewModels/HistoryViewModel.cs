using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;

namespace Optimizer.WinUI.ViewModels;

// ── Wrapper that adds display-only computed properties ───────────────────────
public class HistoryEntryViewModel
{
    private readonly HistoryEntry _entry;

    public HistoryEntryViewModel(HistoryEntry entry) => _entry = entry;

    // Passthrough
    public string        OptimizationId => _entry.OptimizationId;
    public string        Category       => _entry.Category;
    public bool          IsReversible   => _entry.IsReversible;
    public bool          IsUndone       => _entry.IsUndone;
    public HistoryAction Action         => _entry.Action;
    public string?       ResultText     => _entry.ResultText;
    public DateTime      TimestampUtc   => _entry.TimestampUtc;

    // Computed display properties
    public string Title => string.IsNullOrWhiteSpace(_entry.OptimizationTitle)
        ? "(Unnamed optimization)"
        : _entry.OptimizationTitle;

    public string TimeText
        => _entry.TimestampUtc.ToLocalTime().ToString("h:mm tt");

    public string ActionText => _entry.Action switch
    {
        HistoryAction.Applied => "Applied",
        HistoryAction.Undone  => "Undone",
        HistoryAction.OneTime => "One-Time",
        _                     => _entry.Action.ToString()
    };

    /// <summary>True when Applied, reversible, and not yet undone — shows the Undo button.</summary>
    public bool CanUndo
        => _entry.IsReversible && !_entry.IsUndone && _entry.Action == HistoryAction.Applied;

    /// <summary>Hex colour for the action badge background.</summary>
    public string ActionBadgeColor => _entry.Action switch
    {
        HistoryAction.Applied => "#3B82F6",   // blue
        HistoryAction.Undone  => "#6B7280",   // grey
        HistoryAction.OneTime => "#10B981",   // green
        _                     => "#6B7280"
    };
}

// ── Day-group header used by the XAML repeater ───────────────────────────────
public class HistoryEntryGroup
{
    public DateTime Date { get; }
    public List<HistoryEntryViewModel> Entries { get; }

    public HistoryEntryGroup(DateTime date, List<HistoryEntryViewModel> entries)
    {
        Date    = date;
        Entries = entries;
    }

    public string DateHeaderText
    {
        get
        {
            var today = DateTime.Today;
            if (Date.Date == today)              return "Today";
            if (Date.Date == today.AddDays(-1))  return "Yesterday";
            return Date.ToString("dddd, MMMM d, yyyy");
        }
    }

    public int Count => Entries.Count;
}

// ── Main ViewModel ────────────────────────────────────────────────────────────
public partial class HistoryViewModel : ObservableObject
{
    private readonly IHistoryService           _historyService;
    private readonly IWindowsOptimizerService _optimizer;

    // Full unfiltered copy — kept in sync on each Load.
    private List<HistoryEntry> _allEntries = [];

    [ObservableProperty] private bool isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string statusMessage = string.Empty;

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

    // ── Filter properties ─────────────────────────────────────────────────────

    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private string selectedCategory = "All Categories";
    [ObservableProperty] private string selectedAction   = "All Actions";
    [ObservableProperty] private string selectedDateRange = "All Time";

    partial void OnSearchTextChanged(string value)    => ApplyFilters();
    partial void OnSelectedCategoryChanged(string value) => ApplyFilters();
    partial void OnSelectedActionChanged(string value)   => ApplyFilters();
    partial void OnSelectedDateRangeChanged(string value) => ApplyFilters();

    // ── Filter option lists (bound by XAML ComboBoxes) ────────────────────────

    public IReadOnlyList<string> CategoryOptions { get; } =
        new[] { "All Categories", "Performance", "Network", "Storage", "System", "Startup" };

    public IReadOnlyList<string> ActionOptions { get; } =
        new[] { "All Actions", "Applied", "Undone", "One-Time" };

    public IReadOnlyList<string> DateRangeOptions { get; } =
        new[] { "All Time", "Last 24h", "Last 7 days", "Last 30 days" };

    // ── Grouped output ────────────────────────────────────────────────────────

    public ObservableCollection<HistoryEntryGroup> GroupedEntries { get; } = [];

    public string CategoryName => "History";
    public string CategoryIcon => "\U0001F4DC";  // 📜

    public int  TotalCount => GroupedEntries.Sum(g => g.Count);
    public bool IsEmpty    => GroupedEntries.Count == 0;

    public HistoryViewModel(IHistoryService historyService, IWindowsOptimizerService optimizer)
    {
        _historyService = historyService;
        _optimizer      = optimizer;
    }

    // ── Load ─────────────────────────────────────────────────────────────────

    public Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            _allEntries = _historyService.Entries.ToList();
            ApplyFilters();
        }
        finally
        {
            IsLoading = false;
        }
        return Task.CompletedTask;
    }

    // ── Filter logic ──────────────────────────────────────────────────────────

    private void ApplyFilters()
    {
        IEnumerable<HistoryEntry> filtered = _allEntries;

        if (!string.IsNullOrEmpty(SearchText))
            filtered = filtered.Where(e =>
                e.OptimizationTitle.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                e.Category.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        if (SelectedCategory != "All Categories")
            filtered = filtered.Where(e => e.Category == SelectedCategory);

        if (SelectedAction != "All Actions")
        {
            // Map display label to enum — "One-Time" -> HistoryAction.OneTime
            var actionStr = SelectedAction.Replace("-", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal);
            filtered = filtered.Where(e =>
                string.Equals(e.Action.ToString(), actionStr, StringComparison.OrdinalIgnoreCase));
        }

        if (SelectedDateRange != "All Time")
        {
            var cutoff = SelectedDateRange switch
            {
                "Last 24h"    => DateTime.UtcNow.AddDays(-1),
                "Last 7 days" => DateTime.UtcNow.AddDays(-7),
                "Last 30 days"=> DateTime.UtcNow.AddDays(-30),
                _             => DateTime.MinValue
            };
            filtered = filtered.Where(e => e.TimestampUtc >= cutoff);
        }

        var grouped = filtered
            .OrderByDescending(e => e.TimestampUtc)
            .GroupBy(e => e.TimestampUtc.ToLocalTime().Date)
            .Select(g => new HistoryEntryGroup(g.Key, g.Select(e => new HistoryEntryViewModel(e)).ToList()))
            .ToList();

        GroupedEntries.Clear();
        foreach (var g in grouped)
            GroupedEntries.Add(g);

        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(IsEmpty));
    }

    // ── Reset all filters to defaults ─────────────────────────────────────────

    public void ClearFilters()
    {
        SearchText       = "";
        SelectedCategory = "All Categories";
        SelectedAction   = "All Actions";
        SelectedDateRange = "All Time";
        // ApplyFilters() is called implicitly by the last property change.
    }

    // ── Undo a single history entry ──────────────────────────────────────────

    [RelayCommand]
    private async Task UndoEntryAsync(HistoryEntryViewModel entry)
    {
        if (!entry.CanUndo) return;

        StatusMessage = $"Undoing \"{entry.Title}\"…";
        try
        {
            // Audit C5/C12: match every low-level UndoEntry produced by this optimization —
            // exact id first (the new OptimizationId field), Description.Contains only as a
            // fallback for pre-audit entries (null id). Revert newest-first.
            var undoEntries = _optimizer.GetUndoEntries()
                .Where(u => string.Equals(u.OptimizationId, entry.OptimizationId, StringComparison.OrdinalIgnoreCase)
                         || (u.OptimizationId == null &&
                             (u.Description.Contains(entry.OptimizationId, StringComparison.OrdinalIgnoreCase)
                              || u.Description.Contains(entry.Title, StringComparison.OrdinalIgnoreCase))))
                .Reverse()
                .ToList();

            if (undoEntries.Count == 0)
            {
                // Audit C12: do NOT mark it "Undone" — nothing was rolled back.
                StatusMessage = $"No undo snapshot found for \"{entry.Title}\" — left unchanged.";
            }
            else
            {
                var reverted = 0;
                foreach (var u in undoEntries)
                    if (await _optimizer.UndoEntryAsync(u)) reverted++;

                if (reverted > 0)
                {
                    // Only record the undo event when something was actually reverted.
                    _historyService.RecordUndone(entry.OptimizationId, entry.Title, entry.Category);
                    StatusMessage = reverted == undoEntries.Count
                        ? $"Undone: {entry.Title}"
                        : $"Partially undone: {entry.Title} ({reverted} of {undoEntries.Count}).";
                }
                else
                {
                    StatusMessage = $"Could not undo \"{entry.Title}\".";
                }
            }

            await LoadAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error undoing \"{entry.Title}\": {ex.Message}";
        }
    }

    // ── Clear all history ─────────────────────────────────────────────────────

    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        _historyService.Clear();
        StatusMessage = "History cleared.";
        await LoadAsync();
    }
}
