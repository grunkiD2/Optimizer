using System.Text.Json;

namespace Optimizer.WinUI.Services.Assistant;

/// <summary>A tool definition handed to Claude (generated from the command registry).</summary>
public sealed record ClaudeToolDef(string Name, string Description, JsonElement InputSchema);

public enum ClaudeBlockKind { Text, ToolUse, ToolResult }

/// <summary>One content block in a Claude message (our own shape, decoupled from the SDK).</summary>
public sealed record ClaudeBlock(
    ClaudeBlockKind Kind,
    string? Text = null,
    string? ToolUseId = null,
    string? ToolName = null,
    JsonElement ToolInput = default,
    string? ToolResultContent = null,
    bool ToolResultIsError = false);

public sealed record ClaudeMessage(string Role, IReadOnlyList<ClaudeBlock> Content);

/// <summary>Outcome of one Claude turn.</summary>
public sealed record ClaudeTurn(string StopReason, IReadOnlyList<ClaudeBlock> Content);

public enum ClaudeErrorKind { None, Auth, RateLimit, Network, Other }
