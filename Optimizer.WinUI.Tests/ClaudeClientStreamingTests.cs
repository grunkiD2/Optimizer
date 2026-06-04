using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Anthropic;
using Optimizer.WinUI.Services.Assistant;
using Xunit;

namespace Optimizer.WinUI.Tests;

/// <summary>
/// Runtime exercise for the <see cref="ClaudeClient"/> single-pass streaming rewrite.
/// The rest of the assistant suite mocks <see cref="IClaudeClient"/>, so until this file
/// existed the SDK-accumulation code (ContentBlockStart, ContentBlockDelta with text vs
/// input_json, MessageDelta stop_reason) was only build-tested. These tests feed canned
/// SSE bytes into the real Anthropic SDK via an HttpMessageHandler seam.
///
/// The internal ctor (visible via InternalsVisibleTo) lets us inject a pre-built
/// AnthropicClient with a fake handler — see <see cref="ClaudeClient(IApiKeyStore, Func{string, AnthropicClient})"/>.
/// </summary>
public class ClaudeClientStreamingTests
{
    [Fact]
    public async Task Streaming_TextOnly_AccumulatesDeltasAndReturnsStopReason()
    {
        var sse = BuildSse(
            ("message_start", """{"type":"message_start","message":{"id":"msg_1","type":"message","role":"assistant","content":[],"model":"claude-test","stop_reason":null,"stop_sequence":null,"usage":{"input_tokens":10,"output_tokens":0}}}"""),
            ("content_block_start", """{"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}"""),
            ("content_block_delta", """{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Hello"}}"""),
            ("content_block_delta", """{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":" world"}}"""),
            ("content_block_stop", """{"type":"content_block_stop","index":0}"""),
            ("message_delta", """{"type":"message_delta","delta":{"stop_reason":"end_turn","stop_sequence":null},"usage":{"output_tokens":2}}"""),
            ("message_stop", """{"type":"message_stop"}""")
        );

        var collected = new StringBuilder();
        var result = await Run(sse, onText: s => collected.Append(s));

        Assert.Equal(ClaudeErrorKind.None, result.Error);
        Assert.NotNull(result.Turn);
        Assert.Equal("end_turn", result.Turn!.StopReason);
        Assert.Single(result.Turn.Content);
        Assert.Equal(ClaudeBlockKind.Text, result.Turn.Content[0].Kind);
        Assert.Equal("Hello world", result.Turn.Content[0].Text);
        Assert.Equal("Hello world", collected.ToString());
    }

    [Fact]
    public async Task Streaming_ToolUse_AccumulatesPartialJsonAndSurfacesToolBlock()
    {
        var sse = BuildSse(
            ("message_start", """{"type":"message_start","message":{"id":"msg_2","type":"message","role":"assistant","content":[],"model":"claude-test","stop_reason":null,"stop_sequence":null,"usage":{"input_tokens":12,"output_tokens":0}}}"""),
            // Text intro block
            ("content_block_start", """{"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}"""),
            ("content_block_delta", """{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Let me check."}}"""),
            ("content_block_stop", """{"type":"content_block_stop","index":0}"""),
            // Tool-use block — partial JSON accumulates across deltas
            ("content_block_start", """{"type":"content_block_start","index":1,"content_block":{"type":"tool_use","id":"toolu_abc","name":"get_weather","input":{}}}"""),
            ("content_block_delta", """{"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":"{\"loc"}}"""),
            ("content_block_delta", """{"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":"ation\":\"NYC\"}"}}"""),
            ("content_block_stop", """{"type":"content_block_stop","index":1}"""),
            ("message_delta", """{"type":"message_delta","delta":{"stop_reason":"tool_use","stop_sequence":null},"usage":{"output_tokens":18}}"""),
            ("message_stop", """{"type":"message_stop"}""")
        );

        var collected = new StringBuilder();
        var result = await Run(sse, onText: s => collected.Append(s));

        Assert.Equal(ClaudeErrorKind.None, result.Error);
        Assert.NotNull(result.Turn);
        Assert.Equal("tool_use", result.Turn!.StopReason);
        Assert.Equal(2, result.Turn.Content.Count);

        // Text block came first, index 0.
        Assert.Equal(ClaudeBlockKind.Text, result.Turn.Content[0].Kind);
        Assert.Equal("Let me check.", result.Turn.Content[0].Text);
        Assert.Equal("Let me check.", collected.ToString()); // tool-use deltas don't stream to UI

        // Tool-use block at index 1, JSON reassembled from two partial deltas.
        Assert.Equal(ClaudeBlockKind.ToolUse, result.Turn.Content[1].Kind);
        Assert.Equal("toolu_abc", result.Turn.Content[1].ToolUseId);
        Assert.Equal("get_weather", result.Turn.Content[1].ToolName);
        // ToolInput is a JsonElement holding the parsed {"location":"NYC"} object.
        var locationProp = result.Turn.Content[1].ToolInput.GetProperty("location").GetString();
        Assert.Equal("NYC", locationProp);
    }

    [Fact]
    public async Task NoApiKey_ReturnsAuthErrorImmediately_WithoutHittingSdk()
    {
        var keyStore = new FakeKeyStore(null);
        var sentinel = false;
        var client = new ClaudeClient(keyStore, _ => { sentinel = true; return new AnthropicClient { ApiKey = "x" }; });

        var result = await client.SendAsync(
            system: "sys",
            messages: new List<ClaudeMessage>(),
            tools: new List<ClaudeToolDef>(),
            model: "claude-test",
            onText: _ => { },
            ct: CancellationToken.None);

        Assert.Equal(ClaudeErrorKind.Auth, result.Error);
        Assert.Null(result.Turn);
        Assert.False(sentinel, "ClientFactory should not be invoked when no API key is configured.");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static async Task<ClaudeResult> Run(string sseBody, Action<string> onText)
    {
        var fakeHandler = new FakeSseHandler(sseBody);
        // The SDK uses its own HttpClient when AnthropicClient.HttpClient is set.
        var fakeHttp = new HttpClient(fakeHandler);
        var anthropic = new AnthropicClient { ApiKey = "test-key", HttpClient = fakeHttp };
        var keyStore = new FakeKeyStore("test-key");
        var client = new ClaudeClient(keyStore, _ => anthropic);

        return await client.SendAsync(
            system: "test system",
            messages: new List<ClaudeMessage>(),
            tools: new List<ClaudeToolDef>(),
            model: "claude-test",
            onText: onText,
            ct: CancellationToken.None);
    }

    /// <summary>Build a well-formed SSE body from (event, data-json) pairs.
    /// Per the SSE spec each event is terminated by a blank line.</summary>
    private static string BuildSse(params (string Event, string Data)[] events)
    {
        var sb = new StringBuilder();
        foreach (var (e, d) in events)
        {
            sb.Append("event: ").Append(e).Append('\n');
            sb.Append("data: ").Append(d).Append('\n');
            sb.Append('\n'); // event terminator
        }
        return sb.ToString();
    }

    /// <summary>Fake HttpMessageHandler that returns canned SSE bytes for any request.</summary>
    private sealed class FakeSseHandler : HttpMessageHandler
    {
        private readonly byte[] _bytes;
        public FakeSseHandler(string sseBody) => _bytes = Encoding.UTF8.GetBytes(sseBody);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_bytes),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
            return Task.FromResult(response);
        }
    }

    private sealed class FakeKeyStore : IApiKeyStore
    {
        private string? _key;
        public FakeKeyStore(string? key) => _key = key;
        public bool HasKey => !string.IsNullOrEmpty(_key);
        public void SetKey(string apiKey) => _key = apiKey;
        public string? GetKey() => _key;
        public void Clear() => _key = null;
    }
}
