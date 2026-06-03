namespace Optimizer.WinUI.Services.Assistant;

public sealed record ClaudeResult(ClaudeTurn? Turn, ClaudeErrorKind Error, string? ErrorMessage);

public interface IClaudeClient
{
    /// <summary>True if an API key is configured (so the UI can show a setup prompt).</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Run one Claude turn. Streams assistant text via <paramref name="onText"/> as it arrives,
    /// then returns the full turn (including any tool_use blocks) or a mapped error.
    /// </summary>
    Task<ClaudeResult> SendAsync(
        string system,
        IReadOnlyList<ClaudeMessage> messages,
        IReadOnlyList<ClaudeToolDef> tools,
        string model,
        Action<string> onText,
        CancellationToken ct);
}
