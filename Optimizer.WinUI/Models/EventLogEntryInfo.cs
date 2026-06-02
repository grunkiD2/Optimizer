namespace Optimizer.WinUI.Models;

public class EventLogEntryInfo
{
    public DateTime TimeWritten { get; set; }
    public string Source  { get; set; } = "";
    public string LogName { get; set; } = "";
    public int    EventId { get; set; }
    public string Level   { get; set; } = ""; // Critical, Error, Warning, Information

    public string Message { get; set; } = "";

    public string TimeText  => TimeWritten.ToString("MMM d, HH:mm:ss");

    public string LevelColor => Level switch
    {
        "Critical"    => "#DC2626",
        "Error"       => "#EF4444",
        "Warning"     => "#F59E0B",
        _             => "#3B82F6"
    };
}
