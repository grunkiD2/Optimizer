namespace Optimizer.WinUI.Services.Assistant;

/// <summary>
/// Builds the assistant's system prompt, enriching a stable base with the current
/// detected context plus learned signals (reliable tools, successful patterns, liked tools).
/// </summary>
public interface IContextualPromptBuilder
{
    /// <summary>Compose the full system prompt for the current moment.</summary>
    Task<string> BuildAsync();
}
