using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Optimizer.WinUI.Helpers;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;
using Ids = Optimizer.WinUI.Models.OptimizationIds;

namespace Optimizer.WinUI.ViewModels;

public partial class StorageCategoryViewModel : CategoryViewModelBase
{
    private readonly IDiskHealthService _diskHealthService;
    private readonly ICleanupService _cleanupService;

    // ── Basic metrics ──────────────────────────────────────────────────────
    [ObservableProperty] private string diskUsageText = "Calculating…";
    [ObservableProperty] private bool isLoadingDisks;

    public ObservableCollection<DiskHealthInfo> Disks { get; } = [];

    // ── Smart Cleanup ──────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotScanningCleanup))]
    private bool isScanningCleanup;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCleanupProgress))]
    private string cleanupProgress = "";

    [ObservableProperty] private string totalRecoverableText = "";

    public bool IsNotScanningCleanup => !IsScanningCleanup;
    public bool HasCleanupProgress => !string.IsNullOrEmpty(CleanupProgress);

    public ObservableCollection<CleanupCategory> CleanupCategories { get; } = [];

    // ── Large Files ────────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotScanningLargeFiles))]
    private bool isScanningLargeFiles;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLargeFilesProgress))]
    private string largeFilesProgress = "";

    [ObservableProperty] private bool hasLargeFiles;

    public bool IsNotScanningLargeFiles => !IsScanningLargeFiles;
    public bool HasLargeFilesProgress => !string.IsNullOrEmpty(LargeFilesProgress);

    public ObservableCollection<LargeFile> LargeFiles { get; } = [];

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
        IHistoryService history,
        IDiskHealthService diskHealthService,
        ICleanupService cleanupService)
        : base(optimizer, elevation, undoSvc, history)
    {
        _diskHealthService = diskHealthService;
        _cleanupService = cleanupService;
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

    // ── Smart Cleanup commands ─────────────────────────────────────────────

    [RelayCommand]
    public async Task ScanCleanupAsync()
    {
        if (IsScanningCleanup) return;
        IsScanningCleanup = true;
        CleanupProgress = "Scanning…";
        CleanupCategories.Clear();

        try
        {
            var progress = new Progress<string>(msg => CleanupProgress = msg);
            var cats = await _cleanupService.ScanCleanupCategoriesAsync(progress);
            foreach (var c in cats) CleanupCategories.Add(c);
            UpdateTotalRecoverable();
            CleanupProgress = $"Scan complete — {CleanupCategories.Count} categories found.";
        }
        catch (Exception ex)
        {
            CleanupProgress = $"Scan failed: {ex.Message}";
        }
        finally
        {
            IsScanningCleanup = false;
        }
    }

    [RelayCommand]
    public async Task CleanSelectedAsync()
    {
        var selected = CleanupCategories.Where(c => c.Selected).ToList();
        if (selected.Count == 0) return;

        IsScanningCleanup = true;
        long totalCleaned = 0;

        try
        {
            foreach (var cat in selected)
            {
                CleanupProgress = $"Cleaning {cat.Name}…";
                var freed = await _cleanupService.CleanCategoryAsync(cat.Id);
                totalCleaned += freed;
            }
            CleanupProgress = $"Done — freed {ByteFormatter.Format(totalCleaned)}.";
            // Rescan after cleaning
            await ScanCleanupAsync();
        }
        catch (Exception ex)
        {
            CleanupProgress = $"Cleanup error: {ex.Message}";
        }
        finally
        {
            IsScanningCleanup = false;
        }
    }

    private void UpdateTotalRecoverable()
    {
        var total = CleanupCategories.Where(c => c.Selected).Sum(c => c.SizeBytes);
        TotalRecoverableText = total > 0 ? ByteFormatter.Format(total) : "0 B";
    }

    // ── Large Files commands ───────────────────────────────────────────────

    [RelayCommand]
    public async Task ScanLargeFilesAsync(string? rootPath = null)
    {
        if (IsScanningLargeFiles) return;

        rootPath ??= Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
        IsScanningLargeFiles = true;
        LargeFilesProgress = "Scanning…";
        LargeFiles.Clear();
        HasLargeFiles = false;

        try
        {
            var progress = new Progress<string>(msg => LargeFilesProgress = msg);
            var files = await _cleanupService.FindLargeFilesAsync(rootPath, 100_000_000, progress);
            foreach (var f in files) LargeFiles.Add(f);
            HasLargeFiles = LargeFiles.Count > 0;
            LargeFilesProgress = LargeFiles.Count > 0
                ? $"Found {LargeFiles.Count} large files."
                : "No files over 100 MB found.";
        }
        catch (Exception ex)
        {
            LargeFilesProgress = $"Scan failed: {ex.Message}";
        }
        finally
        {
            IsScanningLargeFiles = false;
        }
    }

    /// <summary>Open the containing folder of a large file in Explorer.</summary>
    public static void OpenInExplorer(LargeFile file)
    {
        try
        {
            var folder = Path.GetDirectoryName(file.FullPath);
            if (!string.IsNullOrEmpty(folder))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file.FullPath}\"") { UseShellExecute = true });
        }
        catch { }
    }
}
