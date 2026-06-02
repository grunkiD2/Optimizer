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
        if (enable)
        {
            var result = await Optimizer.ApplyOptimizationAsync(id);
            var info = Optimizer.GetOptimizationInfo(id);
            if (result.Success && info != null)
                History.RecordApplied(id, info.Title, CategoryName, info.Reversible);
        }
        else
        {
            // Undo via IUndoService — find the most recent entry matching this optimization
            var entry = UndoSvc.Entries
                .FirstOrDefault(e =>
                    e.Description.Contains(id, StringComparison.OrdinalIgnoreCase));

            if (entry != null)
            {
                await UndoSvc.UndoAsync(entry);
                var info = Optimizer.GetOptimizationInfo(id);
                if (info != null)
                    History.RecordUndone(id, info.Title, CategoryName);
            }
        }

        Load();
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
