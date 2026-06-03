using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using Optimizer.WinUI.Services;

namespace Optimizer.WinUI.Services.Assistant;

/// <summary>Wraps the official Anthropic .NET SDK behind <see cref="IClaudeClient"/>.</summary>
public sealed class ClaudeClient(IApiKeyStore keyStore) : IClaudeClient
{
    public bool IsConfigured => keyStore.HasKey;

    public async Task<ClaudeResult> SendAsync(
        string system,
        IReadOnlyList<ClaudeMessage> messages,
        IReadOnlyList<ClaudeToolDef> tools,
        string model,
        Action<string> onText,
        CancellationToken ct)
    {
        var key = keyStore.GetKey();
        if (string.IsNullOrWhiteSpace(key))
            return new ClaudeResult(null, ClaudeErrorKind.Auth, "No Anthropic API key is configured.");

        try
        {
            var client = new AnthropicClient { ApiKey = key };

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

            // Stream text deltas to the UI as they arrive.
            await foreach (var streamEvent in client.Messages.CreateStreaming(parameters).WithCancellation(ct))
            {
                if (streamEvent.TryPickContentBlockDelta(out var delta) && delta.Delta.TryPickText(out var text))
                    onText(text.Text);
            }

            // Re-issue non-streaming to get the structured final message (tool_use blocks + stop_reason).
            var final = await client.Messages.Create(parameters);
            var collected = new List<ClaudeBlock>();
            foreach (var block in final.Content)
            {
                if (block.TryPickText(out var txt))
                    collected.Add(new ClaudeBlock(ClaudeBlockKind.Text, Text: txt.Text));
                else if (block.TryPickToolUse(out var tu))
                    collected.Add(new ClaudeBlock(ClaudeBlockKind.ToolUse,
                        ToolUseId: tu.ID, ToolName: tu.Name,
                        ToolInput: JsonSerializer.SerializeToElement(tu.Input)));
            }

            return new ClaudeResult(new ClaudeTurn(final.StopReason ?? "end_turn", collected), ClaudeErrorKind.None, null);
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
