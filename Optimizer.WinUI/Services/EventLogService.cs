using System.Diagnostics.Eventing.Reader;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public class EventLogService : IEventLogService
{
    public Task<IReadOnlyList<EventLogEntryInfo>> GetEntriesAsync(
        string logName,
        int    maxCount    = 200,
        string? levelFilter = null)
    {
        return Task.Run(() =>
        {
            var list = new List<EventLogEntryInfo>();
            try
            {
                var query = levelFilter switch
                {
                    "Critical"    => "*[System[Level=1]]",
                    "Error"       => "*[System[Level=2]]",
                    "Warning"     => "*[System[Level=3]]",
                    "Information" => "*[System[Level=4]]",
                    _             => "*"
                };

                // NEWEST first — the reader's default direction is oldest-first, which made the
                // page show months-old entries as if they were current activity (audit C2).
                using var reader = new EventLogReader(
                    new EventLogQuery(logName, PathType.LogName, query) { ReverseDirection = true });

                EventRecord? record;
                while ((record = reader.ReadEvent()) != null && list.Count < maxCount)
                {
                    try
                    {
                        list.Add(new EventLogEntryInfo
                        {
                            TimeWritten = record.TimeCreated?.ToLocalTime() ?? DateTime.MinValue,
                            Source      = record.ProviderName ?? "",
                            LogName     = logName,
                            EventId     = record.Id,
                            Level       = MapLevel(record.Level),
                            Message     = SafeGetDescription(record)
                        });
                    }
                    catch { /* skip malformed record */ }
                    finally { record.Dispose(); }
                }
            }
            catch (Exception ex)
            {
                EngineLog.Error($"Failed to read '{logName}' event log", ex);
            }

            return (IReadOnlyList<EventLogEntryInfo>)list;
        });
    }

    private static string MapLevel(byte? level) => level switch
    {
        1 => "Critical",
        2 => "Error",
        3 => "Warning",
        4 => "Information",
        _ => "Information"
    };

    private static string SafeGetDescription(EventRecord record)
    {
        try   { return record.FormatDescription() ?? "(no description)"; }
        catch { return "(description unavailable)"; }
    }
}
