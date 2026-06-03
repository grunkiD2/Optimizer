using System.Text.Json;
using Optimizer.WinUI.Services.Commands;

namespace Optimizer.WinUI.Services.Assistant;

public sealed class AssistantService(
    IClaudeClient claude,
    ICommandRegistry registry,
    IAssistantSettings settings,
    IAssistantActionLogger actionLogger,
    IContextDetectionService contextDetection) : IAssistantService
{
    private const int MaxToolRounds = 8;

    private const string SystemPrompt =
        "You are the assistant inside Optimizer, a Windows PC optimization app. " +
        "Use the provided tools to answer questions about the user's PC and to perform actions they request. " +
        "Read-only tools run immediately. Tools that change the system require user confirmation, which the app handles — " +
        "call them normally and the app will prompt the user. Be concise. If a tool returns an error, explain it plainly. " +
        "Prefer calling list_profiles before apply_profile so you use a real id.";

    private readonly List<ClaudeMessage> _history = [];

    public void Reset() => _history.Clear();

    public async Task<string> SendAsync(string userText, AssistantCallbacks cb, CancellationToken ct)
    {
        _history.Add(new ClaudeMessage("user", [new ClaudeBlock(ClaudeBlockKind.Text, Text: userText)]));
        var tools = ToolCatalog.Build(registry, settings.AllowActions);

        for (int round = 0; round < MaxToolRounds; round++)
        {
            var result = await claude.SendAsync(SystemPrompt, _history, tools, settings.Model, cb.OnAssistantText, ct);
            if (result.Error != ClaudeErrorKind.None || result.Turn is null)
            {
                var msg = result.ErrorMessage ?? "The assistant request failed.";
                cb.OnAssistantText(msg);
                return msg;
            }

            var turn = result.Turn;
            _history.Add(new ClaudeMessage("assistant", turn.Content));

            var toolUses = turn.Content.Where(b => b.Kind == ClaudeBlockKind.ToolUse).ToList();
            if (turn.StopReason != "tool_use" || toolUses.Count == 0)
                return JoinText(turn.Content);

            var toolResults = new List<ClaudeBlock>();
            foreach (var use in toolUses)
            {
                var cmd = registry.Find(use.ToolName!);
                if (cmd is null)
                {
                    toolResults.Add(ToolError(use.ToolUseId!, $"Unknown command '{use.ToolName}'."));
                    continue;
                }

                if (cmd.RequiresConfirmation)
                {
                    var summary = $"{cmd.Id} {RenderArgs(use.ToolInput)}".Trim();
                    var approved = await cb.ConfirmAsync(cmd.Id, summary);
                    if (!approved)
                    {
                        toolResults.Add(ToolError(use.ToolUseId!, "User declined this action.", isError: false));
                        continue;
                    }
                }

                cb.OnStatus($"Running {cmd.Id}…");
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var context = await contextDetection.DetectContextAsync();

                try
                {
                    var r = await cmd.ExecuteAsync(use.ToolInput, ct);
                    sw.Stop();

                    // Log action success
                    _ = actionLogger.LogActionAsync(
                        cmd.Id,
                        RenderArgs(use.ToolInput),
                        r.Success,
                        detectedContext: context,
                        executionTimeMs: (int)sw.ElapsedMilliseconds);

                    cb.OnToolExecuted(cmd.Id, r.Success);

                    toolResults.Add(new ClaudeBlock(ClaudeBlockKind.ToolResult,
                        ToolUseId: use.ToolUseId!, ToolResultContent: r.Summary, ToolResultIsError: !r.Success));
                }
                catch (Exception ex)
                {
                    sw.Stop();

                    // Log action failure
                    _ = actionLogger.LogActionAsync(
                        cmd.Id,
                        RenderArgs(use.ToolInput),
                        false,
                        ex.Message,
                        detectedContext: context,
                        executionTimeMs: (int)sw.ElapsedMilliseconds);

                    cb.OnToolExecuted(cmd.Id, false);

                    toolResults.Add(ToolError(use.ToolUseId!, $"Command threw: {ex.Message}"));
                }
            }

            _history.Add(new ClaudeMessage("user", toolResults));
            // loop: feed results back to Claude
        }

        var fallback = "Stopped after too many tool rounds.";
        cb.OnAssistantText(fallback);
        return fallback;
    }

    private static ClaudeBlock ToolError(string id, string text, bool isError = true) =>
        new(ClaudeBlockKind.ToolResult, ToolUseId: id, ToolResultContent: text, ToolResultIsError: isError);

    private static string JoinText(IEnumerable<ClaudeBlock> blocks) =>
        string.Join("", blocks.Where(b => b.Kind == ClaudeBlockKind.Text).Select(b => b.Text)).Trim();

    private static string RenderArgs(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object) return "";
        var parts = input.EnumerateObject().Select(p => $"{p.Name}={p.Value}");
        var joined = string.Join(", ", parts);
        return joined.Length == 0 ? "" : $"({joined})";
    }
}
