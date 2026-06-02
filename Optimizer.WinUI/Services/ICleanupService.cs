namespace Optimizer.WinUI.Services;

public class CleanupCategory
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public long SizeBytes { get; set; }
    public bool Selected { get; set; } = true;
}

public class LargeFile
{
    public string FullPath { get; set; } = "";
    public string FileName { get; set; } = "";
    public long SizeBytes { get; set; }
    public DateTime LastWriteTime { get; set; }
}

public interface ICleanupService
{
    Task<IReadOnlyList<CleanupCategory>> ScanCleanupCategoriesAsync(IProgress<string>? progress = null);
    Task<long> CleanCategoryAsync(string categoryId);
    Task<IReadOnlyList<LargeFile>> FindLargeFilesAsync(string rootPath, long minSizeBytes = 100_000_000, IProgress<string>? progress = null);
}
