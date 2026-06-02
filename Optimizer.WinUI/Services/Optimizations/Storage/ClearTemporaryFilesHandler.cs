using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services.Optimizations.Storage;

public sealed class ClearTemporaryFilesHandler : OptimizationHandlerBase
{
    public override string Id => OptimizationIds.ClearTemporaryFiles;

    public override OptimizationInfo Info { get; } = new()
    {
        Id = "ClearTemporaryFiles",
        Title = "Clear temporary files",
        Summary = "Deletes files in your user TEMP folder that aren't currently in use.",
        Changes = { @"Deletes top-level files in %TEMP% (" + @"e.g. C:\Users\…\AppData\Local\Temp)" },
        Pros = { "Frees disk space", "Removes stale installer/cache leftovers" },
        Cons = { "Cannot be undone", "Skips files locked by running programs" },
        Recommendation = "Safe to run periodically. Close apps first to clear more. NOT reversible — no undo entry is created.",
        SuggestedImplementation = "For a deeper clean, also target the Windows TEMP and Windows Update download caches (requires admin).",
        RequiresAdmin = false,
        Reversible = false
    };

    public override bool? IsApplied() => null; // file-deletion has no persistent state

    public override Task<OptimizationResult> ApplyAsync(IUndoService undoService, IElevationService elevationService)
    {
        var result = new OptimizationResult { Success = true };
        var (deleted, freedBytes) = ClearTemporaryFiles();
        result.Message = $"Cleared {deleted} temporary file(s), freed {freedBytes / 1024 / 1024} MB.";
        result.Warnings.Add("Deleting temporary files cannot be undone.");
        return Task.FromResult(result);
    }

    private static (int deleted, long freedBytes) ClearTemporaryFiles()
    {
        var deleted = 0;
        long freed = 0;
        try
        {
            var tempPath = Path.GetTempPath();
            if (Directory.Exists(tempPath))
            {
                foreach (var file in Directory.GetFiles(tempPath, "*", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        var size = new FileInfo(file).Length;
                        File.Delete(file);
                        deleted++;
                        freed += size;
                    }
                    catch { /* file in use — skip */ }
                }
            }
        }
        catch (Exception ex)
        {
            EngineLog.Write($"Error clearing temporary files: {ex.Message}");
        }
        return (deleted, freed);
    }
}
