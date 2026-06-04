using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using Optimizer.WinUI.Services;

namespace Optimizer.WinUI.Services.Assistant;

/// <summary>Wraps the official Anthropic .NET SDK behind <see cref="IClaudeClient"/>.</summary>
public sealed class ClaudeClient : IClaudeClient
{
    private readonly IApiKeyStore _keyStore;
    private readonly Func<string, AnthropicClient> _clientFactory;

    /// <summary>Production constructor — used by DI. Creates a real AnthropicClient per call.</summary>
    public ClaudeClient(IApiKeyStore keyStore)
        : this(keyStore, key => new AnthropicClient { ApiKey = key }) { }

    /// <summary>Testing seam — lets a test inject an AnthropicClient with a fake HttpMessageHandler.
    /// Internal so it's only reachable through InternalsVisibleTo("Optimizer.WinUI.Tests").</summary>
    internal ClaudeClient(IApiKeyStore keyStore, Func<string, AnthropicClient> clientFactory)
    {
        _keyStore = keyStore;
        _clientFactory = clientFactory;
    }

    public bool IsConfigured => _keyStore.HasKey;

    public async Task<ClaudeResult> SendAsync(
        string system,
        IReadOnlyList<ClaudeMessage> messages,
        IReadOnlyList<ClaudeToolDef> tools,
        string model,
        Action<string> onText,
        CancellationToken ct)
    {
        var key = _keyStore.GetKey();
        if (string.IsNullOrWhiteSpace(key))
            return new ClaudeResult(null, ClaudeErrorKind.Auth, "No Anthropic API key is configured.");

        try
        {
            var client = _clientFactory(key);

            var parameters = new MessageCreateParams
            {
                Model = model,
                MaxTokens = 4096,
                // System prompt cached so repeated turns are cheaper/faster.
                System = new List<TextBlockParam>
                {
                    new() { Text = system, CacheControl = new CacheControlEphemeral() }
                },
                Tools = BuildTools(tools),
                Messages = BuildMessages(messages),
            };

            // Single-pass streaming: forward text deltas to the UI as they arrive AND
            // accumulate tool-use blocks + stop_reason from the same stream. The previous
            // implementation made a second non-streaming Create() call to get tool_use + stop
            // reason, which double-billed every turn. Per the CLAUDE.md gotcha, the streaming
            // events carry everything we need: ContentBlockStart names tool_use blocks,
            // ContentBlockDelta carries TextDelta (for UI) and InputJsonDelta (for tool input),
            // MessageDelta carries the final stop_reason.

            // Per-index state. The model emits blocks 0..N-1 in order; we materialize at the end.
            var blockState = new Dictionary<long, BlockAcc>();
            string stopReason = "end_turn";

            await foreach (var ev in client.Messages.CreateStreaming(parameters, ct).WithCancellation(ct))
            {
                if (ev.TryPickContentBlockStart(out var start))
                {
                    var acc = new BlockAcc();
                    if (start.ContentBlock.TryPickText(out _))
                    {
                        acc.Kind = ClaudeBlockKind.Text;
                    }
                    else if (start.ContentBlock.TryPickToolUse(out var tu))
                    {
                        acc.Kind = ClaudeBlockKind.ToolUse;
                        acc.ToolUseId = tu.ID;
                        acc.ToolName = tu.Name;
                    }
                    else
                    {
                        // Thinking, ServerToolUse, etc. — not surfaced through ClaudeBlock.
                        acc.Kind = ClaudeBlockKind.Text;
                        acc.Skip = true;
                    }
                    blockState[start.Index] = acc;
                }
                else if (ev.TryPickContentBlockDelta(out var delta))
                {
                    if (!blockState.TryGetValue(delta.Index, out var acc)) continue;
                    if (delta.Delta.TryPickText(out var text))
                    {
                        acc.Buffer.Append(text.Text);
                        if (!acc.Skip) onText(text.Text);
                    }
                    else if (delta.Delta.TryPickInputJson(out var inputJson))
                    {
                        acc.Buffer.Append(inputJson.PartialJson);
                    }
                }
                else if (ev.TryPickDelta(out var msgDelta) && msgDelta.Delta.StopReason is { } sr)
                {
                    // ApiEnum<string, StopReason> — null until the final MessageDelta event.
                    string srStr = sr;
                    if (!string.IsNullOrEmpty(srStr)) stopReason = srStr;
                }
                // MessageStart, MessageStop, ContentBlockStop carry no information we need.
            }

            var collected = new List<ClaudeBlock>();
            foreach (var (_, acc) in blockState.OrderBy(kv => kv.Key))
            {
                if (acc.Skip) continue;
                if (acc.Kind == ClaudeBlockKind.Text)
                {
                    collected.Add(new ClaudeBlock(ClaudeBlockKind.Text, Text: acc.Buffer.ToString()));
                }
                else // ToolUse — parse accumulated JSON; default to empty object if the model emitted no args.
                {
                    JsonElement input;
                    var json = acc.Buffer.Length > 0 ? acc.Buffer.ToString() : "{}";
                    try { input = JsonSerializer.Deserialize<JsonElement>(json); }
                    catch (JsonException) { input = JsonSerializer.SerializeToElement(new { }); }
                    collected.Add(new ClaudeBlock(ClaudeBlockKind.ToolUse,
                        ToolUseId: acc.ToolUseId, ToolName: acc.ToolName, ToolInput: input));
                }
            }

            return new ClaudeResult(new ClaudeTurn(stopReason, collected), ClaudeErrorKind.None, null);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            var kind = ex.Message.Contains("401", StringComparison.Ordinal) ? ClaudeErrorKind.Auth
                     : ex.Message.Contains("429", StringComparison.Ordinal) ? ClaudeErrorKind.RateLimit
                     : ex is HttpRequestException ? ClaudeErrorKind.Network
                     : ClaudeErrorKind.Other;
            EngineLog.Error("Claude request failed", ex);   // key is never logged
            return new ClaudeResult(null, kind, FriendlyError(kind));
        }
    }

    private static string FriendlyError(ClaudeErrorKind kind) => kind switch
    {
        ClaudeErrorKind.Auth => "Your Anthropic API key was rejected. Check it in Settings → AI Assistant.",
        ClaudeErrorKind.RateLimit => "Anthropic rate limit hit. Wait a moment and try again.",
        ClaudeErrorKind.Network => "Couldn't reach Anthropic. Check your internet connection.",
        _ => "The assistant request failed. Please try again."
    };

    private static List<ToolUnion> BuildTools(IReadOnlyList<ClaudeToolDef> tools)
    {
        var list = new List<ToolUnion>();
        foreach (var t in tools)
        {
            var props = new Dictionary<string, JsonElement>();
            string[] required = [];
            if (t.InputSchema.TryGetProperty("properties", out var p) && p.ValueKind == JsonValueKind.Object)
                foreach (var prop in p.EnumerateObject())
                    props[prop.Name] = prop.Value;
            if (t.InputSchema.TryGetProperty("required", out var req) && req.ValueKind == JsonValueKind.Array)
                required = req.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0).ToArray();

            list.Add(new Tool
            {
                Name = t.Name,
                Description = t.Description,
                InputSchema = new() { Properties = props, Required = required },
            });
        }
        return list;
    }

    /// <summary>Convert an object JsonElement (our stored tool input) into the dictionary shape the SDK expects.</summary>
    private static Dictionary<string, JsonElement> ToInputDict(JsonElement input)
    {
        var dict = new Dictionary<string, JsonElement>();
        if (input.ValueKind == JsonValueKind.Object)
            foreach (var prop in input.EnumerateObject())
                dict[prop.Name] = prop.Value;
        return dict;
    }

    // Per-block accumulator used while streaming.
    private sealed class BlockAcc
    {
        public ClaudeBlockKind Kind;
        public StringBuilder Buffer { get; } = new();
        public string? ToolUseId;
        public string? ToolName;
        public bool Skip;
    }

    private static List<MessageParam> BuildMessages(IReadOnlyList<ClaudeMessage> messages)
    {
        var result = new List<MessageParam>();
        foreach (var m in messages)
        {
            var blocks = new List<ContentBlockParam>();
            foreach (var b in m.Content)
            {
                switch (b.Kind)
                {
                    case ClaudeBlockKind.Text:
                        blocks.Add(new TextBlockParam { Text = b.Text ?? "" });
                        break;
                    case ClaudeBlockKind.ToolUse:
                        blocks.Add(new ToolUseBlockParam { ID = b.ToolUseId!, Name = b.ToolName!, Input = ToInputDict(b.ToolInput) });
                        break;
                    case ClaudeBlockKind.ToolResult:
                        blocks.Add(new ToolResultBlockParam
                        {
                            ToolUseID = b.ToolUseId!,
                            Content = b.ToolResultContent ?? "",
                            IsError = b.ToolResultIsError,
                        });
                        break;
                }
            }
            result.Add(new MessageParam { Role = m.Role == "assistant" ? Role.Assistant : Role.User, Content = blocks });
        }
        return result;
    }
}
