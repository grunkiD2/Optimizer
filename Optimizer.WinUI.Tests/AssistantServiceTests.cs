using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Optimizer.WinUI.Services.Assistant;
using Optimizer.WinUI.Services.Commands;
using Xunit;

namespace Optimizer.WinUI.Tests;

public class AssistantServiceTests
{
    private sealed class ScriptedClaude(Queue<ClaudeResult> script) : IClaudeClient
    {
        public List<IReadOnlyList<ClaudeToolDef>> ToolsSeen { get; } = [];
        public bool IsConfigured => true;
        public Task<ClaudeResult> SendAsync(string system, IReadOnlyList<ClaudeMessage> messages,
            IReadOnlyList<ClaudeToolDef> tools, string model, Action<string> onText, CancellationToken ct)
        {
            ToolsSeen.Add(tools);
            var next = script.Dequeue();
            foreach (var b in next.Turn?.Content ?? [])
                if (b.Kind == ClaudeBlockKind.Text && b.Text is { } t) onText(t);
            return Task.FromResult(next);
        }
    }

    private sealed class RecordingCommand(string id, bool confirm) : IAppCommand
    {
        public int Executions { get; private set; }
        public string Id => id;
        public string Description => "d";
        public JsonElement ParametersSchema => SchemaJson.Empty;
        public bool IsReadOnly => !confirm;
        public bool RequiresConfirmation => confirm;
        public Task<CommandResult> ExecuteAsync(JsonElement args, CancellationToken ct)
        { Executions++; return Task.FromResult(CommandResult.Ok($"ran {id}")); }
    }

    private sealed class FakeAssistantSettings : IAssistantSettings
    {
        public bool AllowActions { get; set; } = true;
        public string Model { get; set; } = "claude-sonnet-4-6";
    }

    private sealed class NoopActionLogger : IAssistantActionLogger
    {
        public Task LogActionAsync(string toolId, string? arguments, bool success,
            string? errorMessage = null, int executionTimeMs = 0, string? detectedContext = null)
            => Task.CompletedTask;
        public Task<ToolActionMetrics?> GetMetricsAsync(string toolId, string? context = null)
            => Task.FromResult<ToolActionMetrics?>(null);
        public Task<List<AssistantActionLog>> GetRecentActionsAsync(int dayCount = 30)
            => Task.FromResult(new List<AssistantActionLog>());
    }

    private sealed class FakeContextDetection : Optimizer.WinUI.Services.IContextDetectionService
    {
        public Task<string> DetectContextAsync() => Task.FromResult("Unknown");
    }

    private sealed class FakePromptBuilder : IContextualPromptBuilder
    {
        public Task<string> BuildAsync() => Task.FromResult("system prompt");
    }

    private static ClaudeResult Text(string s) =>
        new(new ClaudeTurn("end_turn", [new ClaudeBlock(ClaudeBlockKind.Text, Text: s)]), ClaudeErrorKind.None, null);

    private static ClaudeResult ToolUse(string id, string toolName) =>
        new(new ClaudeTurn("tool_use",
            [new ClaudeBlock(ClaudeBlockKind.ToolUse, ToolUseId: id, ToolName: toolName, ToolInput: SchemaJson.Empty)]),
            ClaudeErrorKind.None, null);

    private static (AssistantService svc, ScriptedClaude claude, RecordingCommand cmd)
        Build(bool confirm, Queue<ClaudeResult> script, bool allowActions = true)
    {
        var reg = new CommandRegistry();
        var cmd = new RecordingCommand("apply_profile", confirm);
        reg.Register(cmd);
        var claude = new ScriptedClaude(script);
        var settings = new FakeAssistantSettings { AllowActions = allowActions };
        return (new AssistantService(claude, reg, settings, new NoopActionLogger(), new FakeContextDetection(), new FakePromptBuilder()), claude, cmd);
    }

    [Fact]
    public async Task Plain_text_turn_returns_text_and_streams()
    {
        var (svc, _, _) = Build(confirm: false, new Queue<ClaudeResult>([Text("Hello there")]));
        var streamed = "";
        var cb = new AssistantCallbacks { OnAssistantText = s => streamed += s };
        var final = await svc.SendAsync("hi", cb, default);
        Assert.Equal("Hello there", final);
        Assert.Equal("Hello there", streamed);
    }

    [Fact]
    public async Task Readonly_tool_executes_without_confirmation()
    {
        var script = new Queue<ClaudeResult>([ToolUse("t1", "apply_profile"), Text("done")]);
        var (svc, _, cmd) = Build(confirm: false, script);
        bool asked = false;
        var cb = new AssistantCallbacks { ConfirmAsync = (_, _) => { asked = true; return Task.FromResult(true); } };
        var final = await svc.SendAsync("go", cb, default);
        Assert.Equal(1, cmd.Executions);
        Assert.False(asked);
        Assert.Equal("done", final);
    }

    [Fact]
    public async Task Confirm_tool_runs_only_after_approval()
    {
        var script = new Queue<ClaudeResult>([ToolUse("t1", "apply_profile"), Text("applied")]);
        var (svc, _, cmd) = Build(confirm: true, script);
        var cb = new AssistantCallbacks { ConfirmAsync = (_, _) => Task.FromResult(true) };
        await svc.SendAsync("apply privacy", cb, default);
        Assert.Equal(1, cmd.Executions);
    }

    [Fact]
    public async Task Confirm_tool_skipped_when_declined()
    {
        var script = new Queue<ClaudeResult>([ToolUse("t1", "apply_profile"), Text("ok, skipped")]);
        var (svc, _, cmd) = Build(confirm: true, script);
        var cb = new AssistantCallbacks { ConfirmAsync = (_, _) => Task.FromResult(false) };
        await svc.SendAsync("apply privacy", cb, default);
        Assert.Equal(0, cmd.Executions);
    }

    [Fact]
    public async Task AllowActions_off_hides_confirm_tools_from_Claude()
    {
        var (svc, claude, _) = Build(confirm: true, new Queue<ClaudeResult>([Text("hi")]), allowActions: false);
        await svc.SendAsync("hi", new AssistantCallbacks(), default);
        Assert.Empty(claude.ToolsSeen[0]);
    }
}
