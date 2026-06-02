using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Optimizer.WinUI.Helpers;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;
using Ids = Optimizer.WinUI.Models.OptimizationIds;

namespace Optimizer.WinUI.ViewModels;

public partial class StorageCategoryViewModel : CategoryViewModelBase
{
    private readonly IDiskHealthService _diskHealthService;

    [ObservableProperty] private string diskUsageText = "Calculating…";
    [ObservableProperty] private bool isLoadingDisks;

    public ObservableCollection<DiskHealthInfo> Disks { get; } = [];

    public override string CategoryName => "Storage";
    public override string CategoryIcon => "💾";

    protected override string[] OptimizationIds =>
    [
        Ids.ClearTemporaryFiles,
        Ids.ClearWindowsUpdateCache
    ];

    public StorageCategoryViewModel(
        IWindowsOptimizerService optimizer,
        IElevationService elevation,
        IUndoService undoSvc,
        HistoryService history,
        IDiskHealthService diskHealthService)
        : base(optimizer, elevation, undoSvc, history)
    {
        _diskHealthService = diskHealthService;
    }

    public override void Load()
    {
        base.Load();
        RefreshMetrics();
    }

    public void RefreshMetrics()
    {
        try
        {
            var tempPath = Path.GetTempPath();
            var tempDir = new DirectoryInfo(tempPath);
            long totalBytes = 0;

            if (tempDir.Exists)
            {
                foreach (var file in tempDir.EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    try { totalBytes += file.Length; }
                    catch { /* skip locked/inaccessible files */ }
                }
            }

            DiskUsageText = ByteFormatter.Format(totalBytes);
        }
        catch
        {
            DiskUsageText = "Unknown";
        }
    }

    public async Task LoadDiskHealthAsync()
    {
        IsLoadingDisks = true;
        try
        {
            var disks = await _diskHealthService.GetDiskHealthAsync();
            Disks.Clear();
            foreach (var d in disks) Disks.Add(d);
        }
        finally
        {
            IsLoadingDisks = false;
        }
    }
}
