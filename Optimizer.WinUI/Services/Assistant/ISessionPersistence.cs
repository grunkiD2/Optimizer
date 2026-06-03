namespace Optimizer.WinUI.Services.Assistant;

/// <summary>Persists and retrieves assistant conversation sessions.</summary>
public interface ISessionPersistence
{
    /// <summary>Get or create today's session.</summary>
    Task<AssistantSession> GetOrCreateTodaySessionAsync();

    /// <summary>Load session events from database.</summary>
    Task<List<SessionEvent>> LoadSessionEventsAsync(string sessionId);

    /// <summary>Append an event to a session.</summary>
    Task AppendEventAsync(string sessionId, SessionEventType eventType, string content);

    /// <summary>Get all sessions with optional date filter.</summary>
    Task<List<AssistantSession>> GetSessionsAsync(DateTime? since = null);

    /// <summary>Archive old sessions (older than N days).</summary>
    Task ArchiveOldSessionsAsync(int olderThanDays = 30);
}

/// <summary>Session metadata.</summary>
public class AssistantSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime SessionDate { get; set; } = DateTime.UtcNow.Date;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ArchivedAtUtc { get; set; }
}

/// <summary>Event within a session (user message, assistant response, tool call, etc.).</summary>
public class SessionEvent
{
    public int Id { get; set; }
    public string SessionId { get; set; } = "";
    public SessionEventType EventType { get; set; }
    public string Content { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Type of event in a conversation session.</summary>
public enum SessionEventType
{
    UserMessage,
    AssistantResponse,
    ToolCall,
    ToolResult,
    Error,
    SystemNotice
}
