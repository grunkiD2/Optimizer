using CommunityToolkit.Mvvm.ComponentModel;
using Optimizer.WinUI.Services;

namespace Optimizer.WinUI.ViewModels;

public partial class StorageCategoryViewModel : CategoryViewModelBase
{
    [ObservableProperty] private string diskUsageText = "Calculating…";

    public override string CategoryName => "Storage";
    public override string CategoryIcon => "💾";

    protected override string[] OptimizationIds =>
    [
        "ClearTemporaryFiles",
        "ClearWindowsUpdateCache"
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

            DiskUsageText = FormatSize(totalBytes);
        }
        catch
        {
            DiskUsageText = "Unknown";
        }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1_073_741_824)
            return $"{bytes / 1_073_741_824.0:F1} GB";
        if (bytes >= 1_048_576)
            return $"{bytes / 1_048_576.0:F0} MB";
        if (bytes >= 1_024)
            return $"{bytes / 1_024.0:F0} KB";
        return $"{bytes} B";
    }
}
