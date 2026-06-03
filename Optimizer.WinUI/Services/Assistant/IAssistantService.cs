namespace Optimizer.WinUI.Services.Assistant;

/// <summary>UI callbacks the orchestrator uses to stream output and request confirmation.</summary>
public sealed class AssistantCallbacks
{
    /// <summary>Called with each streamed assistant text delta.</summary>
    public Action<string> OnAssistantText { get; init; } = _ => { };

    /// <summary>Called once per turn with a short status (e.g. "Running get_metrics…").</summary>
    public Action<string> OnStatus { get; init; } = _ => { };

    /// <summary>
    /// Called before a confirm-required command runs. Return true to execute, false to decline.
    /// Args summary is human-readable (command id + arguments).
    /// </summary>
    public Func<string, string, Task<bool>> ConfirmAsync { get; init; } = (_, _) => Task.FromResult(false);
}

public interface IAssistantService
{
    /// <summary>
    /// Send a user message and drive the tool-use loop to completion.
    /// Maintains conversation state across calls. Returns the final assistant text.
    /// </summary>
    Task<string> SendAsync(string userText, AssistantCallbacks callbacks, CancellationToken ct);

    /// <summary>Clear the conversation history.</summary>
    void Reset();
}
