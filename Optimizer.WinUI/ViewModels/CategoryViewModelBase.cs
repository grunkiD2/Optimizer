using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Optimizer.WinUI.Services;

namespace Optimizer.WinUI.ViewModels;

public abstract partial class CategoryViewModelBase : ObservableObject, ICategoryViewModel
{
    protected readonly IWindowsOptimizerService Optimizer;
    protected readonly IElevationService Elevation;
    protected readonly IUndoService UndoSvc;
    protected readonly IHistoryService History;

    [ObservableProperty] private int activeCount;
    [ObservableProperty] private int totalCount;
    /// <summary>Last action result, surfaced as an InfoBar on the category pages (audit Batch 2 —
    /// apply/undo no longer fail silently). Empty hides the bar.</summary>
    [ObservableProperty] private string statusMessage = "";
    [ObservableProperty] private bool statusIsError;

    /// <summary>True when there is a status message to show (drives the InfoBar's IsOpen).</summary>
    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusMessage);

    partial void OnStatusMessageChanged(string value) => OnPropertyChanged(nameof(HasStatus));

    public ObservableCollection<OptimizationCardModel> Optimizations { get; } = [];

    public abstract string CategoryName { get; }
    public abstract string CategoryIcon { get; }
    protected abstract string[] OptimizationIds { get; }

    protected CategoryViewModelBase(
        IWindowsOptimizerService optimizer,
        IElevationService elevation,
        IUndoService undoSvc,
        IHistoryService history)
    {
        Optimizer = optimizer;
        Elevation = elevation;
        UndoSvc = undoSvc;
        History = history;
    }

    public virtual void Load()
    {
        Optimizations.Clear();
        foreach (var id in OptimizationIds)
        {
            var info = Optimizer.GetOptimizationInfo(id);
            if (info == null) continue;

            // IsOptimizationApplied returns bool? — treat null as false
            var isActive = Optimizer.IsOptimizationApplied(id) == true;

            Optimizations.Add(new OptimizationCardModel
            {
                Id = id,
                Info = info,
                IsActive = isActive,
                IsElevated = Elevation.IsElevated
            });
        }

        TotalCount = Optimizations.Count;
        ActiveCount = Optimizations.Count(o => o.IsActive);
    }

    public async Task ToggleOptimizationAsync(string id, bool enable)
    {
        var info = Optimizer.GetOptimizationInfo(id);
        var title = info?.Title ?? id;

        if (enable)
        {
            var result = await Optimizer.ApplyOptimizationAsync(id);
            if (result.Success)
            {
                if (info != null) History.RecordApplied(id, info.Title, CategoryName, info.Reversible);
                SetStatus(string.IsNullOrWhiteSpace(result.Message) ? $"Applied: {title}" : result.Message, false);
            }
            else
            {
                // Audit Batch 2: apply failures (typically missing elevation) used to flip the
                // card back silently. Surface the reason.
                var reason = result.Errors?.Count > 0 ? string.Join("; ", result.Errors)
                    : (string.IsNullOrWhiteSpace(result.Message) ? "Apply failed." : result.Message);
                SetStatus($"{title}: {reason}", true);
            }
        }
        else
        {
            // Audit C5: revert EVERY entry produced by this optimization (an optimization can
            // write several registry values), newest first. Exact id-match, with a
            // Description.Contains fallback only for pre-audit undo.json entries (null id).
            var entries = UndoSvc.Entries
                .Where(e => string.Equals(e.OptimizationId, id, StringComparison.OrdinalIgnoreCase)
                         || (e.OptimizationId == null && e.Description.Contains(id, StringComparison.OrdinalIgnoreCase)))
                .Reverse()
                .ToList();

            if (entries.Count == 0)
            {
                SetStatus($"Nothing to undo for {title} (no captured snapshot).", true);
            }
            else
            {
                var reverted = 0;
                foreach (var entry in entries)
                    if (await UndoSvc.UndoAsync(entry)) reverted++;

                if (reverted > 0 && info != null) History.RecordUndone(id, info.Title, CategoryName);
                SetStatus(reverted == entries.Count
                    ? $"Reverted: {title}"
                    : $"{title}: reverted {reverted} of {entries.Count} change(s) — see log.", reverted != entries.Count);
            }
        }

        Load();
    }

    protected void SetStatus(string message, bool isError)
    {
        StatusMessage = message;
        StatusIsError = isError;
    }

    [RelayCommand]
    public async Task ApplyAll()
    {
        foreach (var opt in Optimizations.Where(o => !o.IsActive).ToList())
            await ToggleOptimizationAsync(opt.Id, true);
    }

    [RelayCommand]
    public async Task UndoCategory()
    {
        foreach (var opt in Optimizations.Where(o => o.IsActive).ToList())
            await ToggleOptimizationAsync(opt.Id, false);
    }
}
