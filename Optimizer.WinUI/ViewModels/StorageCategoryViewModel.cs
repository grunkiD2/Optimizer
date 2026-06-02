using CommunityToolkit.Mvvm.ComponentModel;
using Optimizer.WinUI.Helpers;
using Optimizer.WinUI.Services;
using Ids = Optimizer.WinUI.Models.OptimizationIds;

namespace Optimizer.WinUI.ViewModels;

public partial class StorageCategoryViewModel : CategoryViewModelBase
{
    [ObservableProperty] private string diskUsageText = "Calculating…";

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
        HistoryService history)
        : base(optimizer, elevation, undoSvc, history)
    {
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
}
