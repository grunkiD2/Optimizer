namespace Optimizer.WinUI.Services;

/// <summary>
/// Persists daily samples of drive-space and disk-SMART metrics for multi-day trend analysis.
/// All data stays on-device (%LocalAppData%\Optimizer\trend-history.json).
/// </summary>
public interface ITrendHistoryService
{
    /// <summary>
    /// Records today's sample (drive free space, SMART wear/temp, memory).
    /// Deduplicates to one sample per calendar day (updates if called again same day).
    /// </summary>
    Task RecordSampleAsync();

    /// <summary>Returns (date, freeBytes) pairs for the given drive letter (e.g. "C").</summary>
    IReadOnlyList<(DateTime Date, long FreeBytes)> GetDriveFreeHistory(string driveLetter);

    /// <summary>Returns (date, wearPercent) pairs for the disk with the given serial number.</summary>
    IReadOnlyList<(DateTime Date, int WearPercent)> GetDiskWearHistory(string serial);
}
