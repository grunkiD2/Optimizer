using System.Runtime.InteropServices;

namespace Optimizer.WinUI.Services;

public class CleanupService : ICleanupService
{
    public async Task<IReadOnlyList<CleanupCategory>> ScanCleanupCategoriesAsync(IProgress<string>? progress = null)
    {
        EngineLog.Write("[CleanupService] Scanning cleanup categories");
        var categories = new List<CleanupCategory>();

        progress?.Report("Scanning temp files...");
        categories.Add(new CleanupCategory
        {
            Id = "temp-user",
            Name = "User Temp Files",
            Description = "%TEMP% folder",
            SizeBytes = await GetDirectorySizeAsync(Path.GetTempPath())
        });

        progress?.Report("Scanning Windows temp...");
        categories.Add(new CleanupCategory
        {
            Id = "temp-windows",
            Name = "Windows Temp Files",
            Description = @"C:\Windows\Temp",
            SizeBytes = await GetDirectorySizeAsync(@"C:\Windows\Temp")
        });

        progress?.Report("Checking Edge cache...");
        var edgeCache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            @"Microsoft\Edge\User Data\Default\Cache");
        if (Directory.Exists(edgeCache))
            categories.Add(new CleanupCategory
            {
                Id = "edge-cache",
                Name = "Edge Browser Cache",
                Description = "Microsoft Edge",
                SizeBytes = await GetDirectorySizeAsync(edgeCache)
            });

        progress?.Report("Checking Chrome cache...");
        var chromeCache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            @"Google\Chrome\User Data\Default\Cache");
        if (Directory.Exists(chromeCache))
            categories.Add(new CleanupCategory
            {
                Id = "chrome-cache",
                Name = "Chrome Browser Cache",
                Description = "Google Chrome",
                SizeBytes = await GetDirectorySizeAsync(chromeCache)
            });

        progress?.Report("Checking Firefox cache...");
        var ffProfiles = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            @"Mozilla\Firefox\Profiles");
        if (Directory.Exists(ffProfiles))
        {
            long ffSize = 0;
            foreach (var dir in Directory.GetDirectories(ffProfiles))
            {
                var cacheDir = Path.Combine(dir, "cache2");
                if (Directory.Exists(cacheDir)) ffSize += await GetDirectorySizeAsync(cacheDir);
            }
            if (ffSize > 0)
                categories.Add(new CleanupCategory
                {
                    Id = "firefox-cache",
                    Name = "Firefox Browser Cache",
                    Description = "Mozilla Firefox",
                    SizeBytes = ffSize
                });
        }

        progress?.Report("Checking recycle bin...");
        long binSize = 0;
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
        {
            var bin = Path.Combine(drive.Name, "$Recycle.Bin");
            if (Directory.Exists(bin))
                try { binSize += await GetDirectorySizeAsync(bin); } catch { }
        }
        categories.Add(new CleanupCategory
        {
            Id = "recycle-bin",
            Name = "Recycle Bin",
            Description = "All drives",
            SizeBytes = binSize
        });

        progress?.Report("Checking thumbnail cache...");
        var thumbs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            @"Microsoft\Windows\Explorer");
        if (Directory.Exists(thumbs))
        {
            long thumbSize = Directory.EnumerateFiles(thumbs, "thumbcache_*.db")
                .Sum(f => { try { return new FileInfo(f).Length; } catch { return 0L; } });
            if (thumbSize > 0)
                categories.Add(new CleanupCategory
                {
                    Id = "thumb-cache",
                    Name = "Thumbnail Cache",
                    Description = "Explorer thumbnails",
                    SizeBytes = thumbSize
                });
        }

        progress?.Report("Checking crash dumps...");
        if (Directory.Exists(@"C:\Windows\Minidump"))
        {
            long dumpSize = await GetDirectorySizeAsync(@"C:\Windows\Minidump");
            if (dumpSize > 0)
                categories.Add(new CleanupCategory
                {
                    Id = "minidumps",
                    Name = "Crash Dump Files",
                    Description = "Windows minidumps",
                    SizeBytes = dumpSize
                });
        }

        progress?.Report("Checking Windows logs...");
        if (Directory.Exists(@"C:\Windows\Logs"))
        {
            long logSize = await GetDirectorySizeAsync(@"C:\Windows\Logs");
            categories.Add(new CleanupCategory
            {
                Id = "windows-logs",
                Name = "Windows Logs",
                Description = @"C:\Windows\Logs",
                SizeBytes = logSize
            });
        }

        progress?.Report("Checking Windows.old...");
        if (Directory.Exists(@"C:\Windows.old"))
        {
            long oldSize = await GetDirectorySizeAsync(@"C:\Windows.old");
            categories.Add(new CleanupCategory
            {
                Id = "windows-old",
                Name = "Previous Windows Installation",
                Description = "Windows.old folder",
                SizeBytes = oldSize
            });
        }

        return categories;
    }

    public async Task<long> CleanCategoryAsync(string categoryId)
    {
        EngineLog.Write($"[CleanupService] Cleaning category: {categoryId}");
        var bytesDeleted = await Task.Run(() =>
        {
            if (categoryId == "recycle-bin")
            {
                try { SHEmptyRecycleBin(IntPtr.Zero, null, 0x1 | 0x2 | 0x4); } catch { }
                return 0L;
            }

            if (categoryId == "thumb-cache")
            {
                var thumbs = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Microsoft\Windows\Explorer");
                long deleted = 0;
                foreach (var f in Directory.EnumerateFiles(thumbs, "thumbcache_*.db"))
                {
                    try { deleted += new FileInfo(f).Length; File.Delete(f); } catch { }
                }
                return deleted;
            }

            string? path = categoryId switch
            {
                "temp-user" => Path.GetTempPath(),
                "temp-windows" => @"C:\Windows\Temp",
                "edge-cache" => Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Microsoft\Edge\User Data\Default\Cache"),
                "chrome-cache" => Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Google\Chrome\User Data\Default\Cache"),
                "minidumps" => @"C:\Windows\Minidump",
                "windows-logs" => @"C:\Windows\Logs",
                _ => null
            };

            if (path == null || !Directory.Exists(path)) return 0L;

            long bytesDeleted = 0;
            try
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        var size = new FileInfo(file).Length;
                        File.Delete(file);
                        bytesDeleted += size;
                    }
                    catch { /* locked file, skip */ }
                }
            }
            catch { }
            return bytesDeleted;
        });
        EngineLog.Write($"[CleanupService] Cleaned '{categoryId}': {bytesDeleted / 1024 / 1024} MB freed");
        return bytesDeleted;
    }

    public async Task<IReadOnlyList<LargeFile>> FindLargeFilesAsync(
        string rootPath,
        long minSizeBytes = 100_000_000,
        IProgress<string>? progress = null)
    {
        EngineLog.Write($"[CleanupService] Finding large files (>{minSizeBytes / 1024 / 1024} MB) under {rootPath}");
        return await Task.Run(() =>
        {
            var results = new List<LargeFile>();
            try
            {
                progress?.Report($"Scanning {rootPath}...");
                var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true };
                foreach (var file in Directory.EnumerateFiles(rootPath, "*", options))
                {
                    try
                    {
                        var fi = new FileInfo(file);
                        if (fi.Length >= minSizeBytes)
                        {
                            results.Add(new LargeFile
                            {
                                FullPath = file,
                                FileName = fi.Name,
                                SizeBytes = fi.Length,
                                LastWriteTime = fi.LastWriteTime
                            });
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return (IReadOnlyList<LargeFile>)results
                .OrderByDescending(f => f.SizeBytes)
                .Take(100)
                .ToList();
        });
    }

    private static async Task<long> GetDirectorySizeAsync(string path)
    {
        return await Task.Run(() =>
        {
            if (!Directory.Exists(path)) return 0L;
            try
            {
                var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true };
                return Directory.EnumerateFiles(path, "*", options)
                    .Sum(f => { try { return new FileInfo(f).Length; } catch { return 0L; } });
            }
            catch { return 0L; }
        });
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? rootPath, uint flags);
}
