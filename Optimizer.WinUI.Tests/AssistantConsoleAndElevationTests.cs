using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.Services.Assistant;
using Optimizer.WinUI.Services.Commands;
using Xunit;

namespace Optimizer.WinUI.Tests;

/// <summary>
/// Regression tests for two assistant defects observed 2026-06-04:
///   • Bug A — nothing was written to the Activity console while the assistant ran tools.
///   • Bug B — every tool call required user approval even when the whole process was
///     already elevated.
/// </summary>
public class AssistantConsoleAndElevationTests
{
    // ── shared test plumbing ────────────────────────────────────────────────

    private sealed class FakeClaude(Queue<ClaudeResult> script) : IClaudeClient
    {
        public bool IsConfigured => true;
        public Task<ClaudeResult> SendAsync(string sys, IReadOnlyList<ClaudeMessage> hist,
            IReadOnlyList<ClaudeToolDef> tools, string model, Action<string> onText, CancellationToken ct)
            => Task.FromResult(script.Dequeue());
    }

    private sealed class NoopLogger : IAssistantActionLogger
    {
        public Task LogActionAsync(string toolId, string? args, bool success,
            string? errorMessage = null, int executionTimeMs = 0, string? detectedContext = null) => Task.CompletedTask;
        public Task<ToolActionMetrics?> GetMetricsAsync(string toolId, string? context = null)
            => Task.FromResult<ToolActionMetrics?>(null);
        public Task<List<AssistantActionLog>> GetRecentActionsAsync(int limit = 50)
            => Task.FromResult(new List<AssistantActionLog>());
    }

    private sealed class NoContext : IContextDetectionService
    {
        public Task<string> DetectContextAsync() => Task.FromResult("Unknown");
        public UserIntent UserIntent => UserIntent.None;
    }

    private sealed class StubPromptBuilder : IContextualPromptBuilder
    {
        public Task<string> BuildAsync() => Task.FromResult("test");
    }

    private sealed class StubSettings(bool autoConfirmWhenElevated = false) : IAssistantSettings
    {
        public bool AllowActions => true;
        public string Model => "claude-sonnet-4-6";
        public bool AutoConfirmWhenElevated { get; } = autoConfirmWhenElevated;
    }

    private sealed class StubElevation(bool isElevated) : IElevationService
    {
        public bool IsElevated { get; } = isElevated;
        public bool TryRelaunchElevated() => false;
    }

    private sealed class RecordingCmd(string id, bool requiresConfirm) : IAppCommand
    {
        public string Id { get; } = id;
        public string Description => "test cmd";
        public JsonElement ParametersSchema { get; } = JsonDocument.Parse("{}").RootElement;
        public bool IsReadOnly => !requiresConfirm;
        public bool RequiresConfirmation { get; } = requiresConfirm;
        public int Executions;
        public Task<CommandResult> ExecuteAsync(JsonElement args, CancellationToken ct)
        {
            Executions++;
            return Task.FromResult(CommandResult.Ok("did the thing"));
        }
    }

    private static ClaudeResult ToolUse(string id, string toolName) =>
        new(new ClaudeTurn("tool_use",
            [new ClaudeBlock(ClaudeBlockKind.ToolUse, ToolUseId: id, ToolName: toolName, ToolInput: SchemaJson.Empty)]),
            ClaudeErrorKind.None, null);

    private static ClaudeResult Text(string s) =>
        new(new ClaudeTurn("end_turn", [new ClaudeBlock(ClaudeBlockKind.Text, Text: s)]), ClaudeErrorKind.None, null);

    // ── Bug A — Activity console is fed when the assistant runs tools ───────

    [Fact]
    public async Task Tool_call_emits_engine_log_lines_so_activity_console_is_not_empty()
    {
        var captured = new List<string>();
        void Sink(string msg, Exception? _) => captured.Add(msg);
        EngineLog.LineWritten += Sink;
        try
        {
            var script = new Queue<ClaudeResult>([ToolUse("t1", "test_cmd"), Text("done")]);
            var reg = new CommandRegistry();
            var cmd = new RecordingCmd("test_cmd", requiresConfirm: false);
            reg.Register(cmd);

            var svc = new AssistantService(
                new FakeClaude(script), reg, new StubSettings(), new NoopLogger(),
                new NoContext(), new StubPromptBuilder(), new StubElevation(isElevated: false));

            await svc.SendAsync("do it", new AssistantCallbacks(), default);

            // At least the "▸ tool starting" and "✓ tool succeeded" lines should land.
            Assert.Contains(captured, m => m.Contains("Assistant ▸ test_cmd"));
            Assert.Contains(captured, m => m.Contains("Assistant ✓ test_cmd"));
            Assert.Equal(1, cmd.Executions);
        }
        finally { EngineLog.LineWritten -= Sink; }
    }

    // ── Bug B — Elevated trust skips per-tool confirmation dialogs ──────────

    [Fact]
    public async Task Elevated_trust_skips_confirm_dialog_for_mutating_tool()
    {
        var script = new Queue<ClaudeResult>([ToolUse("t1", "mutating_cmd"), Text("done")]);
        var reg = new CommandRegistry();
        var cmd = new RecordingCmd("mutating_cmd", requiresConfirm: true);
        reg.Register(cmd);

        bool wasAsked = false;
        var cb = new AssistantCallbacks { ConfirmAsync = (_, _) => { wasAsked = true; return Task.FromResult(true); } };

        var svc = new AssistantService(
            new FakeClaude(script), reg,
            new StubSettings(autoConfirmWhenElevated: true), new NoopLogger(),
            new NoContext(), new StubPromptBuilder(),
            new StubElevation(isElevated: true));

        await svc.SendAsync("do it", cb, default);

        Assert.False(wasAsked, "Confirm dialog should NOT fire when both elevated AND AutoConfirmWhenElevated=true");
        Assert.Equal(1, cmd.Executions);
    }

    [Fact]
    public async Task Unelevated_still_prompts_confirm_even_with_trust_toggle_on()
    {
        var script = new Queue<ClaudeResult>([ToolUse("t1", "mutating_cmd"), Text("done")]);
        var reg = new CommandRegistry();
        var cmd = new RecordingCmd("mutating_cmd", requiresConfirm: true);
        reg.Register(cmd);

        bool wasAsked = false;
        var cb = new AssistantCallbacks { ConfirmAsync = (_, _) => { wasAsked = true; return Task.FromResult(true); } };

        var svc = new AssistantService(
            new FakeClaude(script), reg,
            new StubSettings(autoConfirmWhenElevated: true), new NoopLogger(),
            new NoContext(), new StubPromptBuilder(),
            new StubElevation(isElevated: false));  // ← not elevated

        await svc.SendAsync("do it", cb, default);

        Assert.True(wasAsked, "When not elevated, the confirm dialog must still fire regardless of the trust toggle");
    }

    [Fact]
    public async Task Elevated_but_trust_off_still_prompts_confirm()
    {
        var script = new Queue<ClaudeResult>([ToolUse("t1", "mutating_cmd"), Text("done")]);
        var reg = new CommandRegistry();
        var cmd = new RecordingCmd("mutating_cmd", requiresConfirm: true);
        reg.Register(cmd);

        bool wasAsked = false;
        var cb = new AssistantCallbacks { ConfirmAsync = (_, _) => { wasAsked = true; return Task.FromResult(true); } };

        var svc = new AssistantService(
            new FakeClaude(script), reg,
            new StubSettings(autoConfirmWhenElevated: false), new NoopLogger(),
            new NoContext(), new StubPromptBuilder(),
            new StubElevation(isElevated: true));

        await svc.SendAsync("do it", cb, default);

        Assert.True(wasAsked, "Elevation alone shouldn't grant trust — the user must opt in via the setting");
    }
}
