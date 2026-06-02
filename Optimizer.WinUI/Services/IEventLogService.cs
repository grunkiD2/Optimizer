using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public interface IEventLogService
{
    Task<IReadOnlyList<EventLogEntryInfo>> GetEntriesAsync(
        string logName,
        int    maxCount    = 200,
        string? levelFilter = null);
}
