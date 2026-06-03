namespace Optimizer.WinUI.Services.Analytics;

/// <summary>Records and aggregates user feedback on assistant actions.</summary>
public interface IAssistantFeedbackService
{
    /// <summary>Record a thumbs up/down (and optional comment) for a tool used in a session.</summary>
    Task RecordFeedbackAsync(string? sessionId, string toolId, FeedbackVerdict verdict, string? comment = null);

    /// <summary>Net feedback score for a tool (likes minus dislikes). Higher is more liked.</summary>
    Task<int> GetNetScoreAsync(string toolId);

    /// <summary>Get recent feedback entries.</summary>
    Task<List<AssistantFeedbackEntry>> GetRecentFeedbackAsync(int count = 50);
}

/// <summary>User's verdict on an assistant action.</summary>
public enum FeedbackVerdict
{
    Liked,
    Disliked
}

/// <summary>A single feedback record.</summary>
public class AssistantFeedbackEntry
{
    public int Id { get; set; }
    public string? SessionId { get; set; }
    public string ToolId { get; set; } = "";
    public FeedbackVerdict Verdict { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
