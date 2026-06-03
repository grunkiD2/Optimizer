using System.Text.Json;

namespace Optimizer.WinUI.Services.Commands;

/// <summary>Result of executing an app command. Summary is human-readable and fed back to Claude as the tool_result.</summary>
public record CommandResult(bool Success, string Summary, object? Data = null)
{
    public static CommandResult Ok(string summary, object? data = null) => new(true, summary, data);
    public static CommandResult Fail(string summary) => new(false, summary);
}

/// <summary>A single invokable app capability. Self-describes for Claude tool generation and carries safety metadata.</summary>
public interface IAppCommand
{
    /// <summary>Stable snake_case id; doubles as the Claude tool name (e.g. "apply_profile").</summary>
    string Id { get; }

    /// <summary>Human-readable description used as the Claude tool description.</summary>
    string Description { get; }

    /// <summary>JSON Schema (object) for the tool input. Use an empty-object schema for no parameters.</summary>
    JsonElement ParametersSchema { get; }

    /// <summary>True if the command never changes the system (queries/navigation).</summary>
    bool IsReadOnly { get; }

    /// <summary>True if the command must be confirmed by the user before executing.</summary>
    bool RequiresConfirmation { get; }

    Task<CommandResult> ExecuteAsync(JsonElement args, CancellationToken ct);
}
