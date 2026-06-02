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
    private readonly HistoryService           _historyService;
    private readonly IWindowsOptimizerService _optimizer;

    [ObservableProperty] private bool   isLoading;
    [ObservableProperty] private string statusMessage = string.Empty;

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

    partial void OnStatusMessageChanged(string value)
        => OnPropertyChanged(nameof(HasStatusMessage));

    public ObservableCollection<HistoryEntryGroup> GroupedEntries { get; } = [];

    public string CategoryName => "History";
    public string CategoryIcon => "\U0001F4DC";  // 📜

    public int  TotalCount => GroupedEntries.Sum(g => g.Count);
    public bool IsEmpty    => GroupedEntries.Count == 0;

    public HistoryViewModel(HistoryService historyService, IWindowsOptimizerService optimizer)
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
            // Entries are already in most-recent-first order (each RecordX inserts at 0).
            var grouped = _historyService.Entries
                .Select(e => new HistoryEntryViewModel(e))
                .GroupBy(vm => vm.TimestampUtc.ToLocalTime().Date)
                .Select(g => new HistoryEntryGroup(g.Key, g.ToList()))
                .ToList();

            GroupedEntries.Clear();
            foreach (var g in grouped)
                GroupedEntries.Add(g);

            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(IsEmpty));
        }
        finally
        {
            IsLoading = false;
        }
        return Task.CompletedTask;
    }

    // ── Undo a single history entry ──────────────────────────────────────────

    [RelayCommand]
    private async Task UndoEntryAsync(HistoryEntryViewModel entry)
    {
        if (!entry.CanUndo) return;

        StatusMessage = $"Undoing \"{entry.Title}\"…";
        try
        {
            // Try to find the matching low-level UndoEntry by description or id.
            var undoEntry = _optimizer.GetUndoEntries()
                .FirstOrDefault(u =>
                    u.Description.Contains(entry.OptimizationId, StringComparison.OrdinalIgnoreCase)
                    || u.Description.Contains(entry.Title, StringComparison.OrdinalIgnoreCase));

            if (undoEntry != null)
            {
                var ok = await _optimizer.UndoEntryAsync(undoEntry);
                StatusMessage = ok
                    ? $"Undone: {entry.Title}"
                    : $"Could not undo \"{entry.Title}\".";
            }
            else
            {
                // No low-level undo snapshot found; just record it as undone in history.
                StatusMessage = $"No undo snapshot found for \"{entry.Title}\"; recorded as undone.";
            }

            // Always record the undo event in history so the UI stays consistent.
            _historyService.RecordUndone(entry.OptimizationId, entry.Title, entry.Category);
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
