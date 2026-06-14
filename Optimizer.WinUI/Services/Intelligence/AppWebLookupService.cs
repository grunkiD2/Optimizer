// AppWebLookupService.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Anthropic;
using Anthropic.Models.Messages;
using Optimizer.WinUI.Services.Assistant;

namespace Optimizer.WinUI.Services.Intelligence;

/// <summary>
/// External "~ ekstern" tier: a confidence-marked, URL-cited web lookup of per-app facts (frame-cap,
/// Reflex, HDR support, gotchas) via Claude's server-side web_search tool. Reuses the existing Anthropic
/// key (IApiKeyStore). Cached per exe, non-blocking, fail-safe (no key/offline → empty). Measurement wins.
/// </summary>
public sealed class AppWebLookupService : IAppWebLookup
{
    private readonly Func<bool> _isConfigured;
    // Mutable (not readonly) so the production ctor can bind it to FetchViaAnthropic AFTER 'this' exists,
    // without the reflection hack. The testing ctor injects a stub directly.
    private Func<string, CancellationToken, Task<IReadOnlyList<EvidenceLine>>> _fetch;
    private readonly string _model;
    private readonly ConcurrentDictionary<string, IReadOnlyList<EvidenceLine>> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Testing/orchestration ctor — inject the fetch + configured-check.</summary>
    public AppWebLookupService(
        Func<bool> isConfigured,
        Func<string, CancellationToken, Task<IReadOnlyList<EvidenceLine>>> fetch,
        string model)
    {
        _isConfigured = isConfigured;
        _fetch = fetch;
        _model = model;
    }

    /// <summary>Production ctor — binds fetch to the real Anthropic web_search call. Uses the existing
    /// Anthropic key (IApiKeyStore); the tier is empty/no-op without a key, so DI-registration is safe.</summary>
    public AppWebLookupService(IApiKeyStore keyStore, string model = "claude-opus-4-8")
    {
        _isConfigured = () => keyStore.HasKey;
        _model = model;
        // Clean alternative to the reflection hack: assign _fetch in the ctor body now that 'this' exists.
        _fetch = (exe, ct) => FetchViaAnthropic(keyStore, exe, ct);
    }

    public IReadOnlyList<EvidenceLine> CachedFor(string exe)
        => _cache.TryGetValue(exe, out var v) ? v : Array.Empty<EvidenceLine>();

    /// <summary>Fetch external facts for an exe and cache them. Never throws; no-ops if not configured
    /// or already cached. Call this opportunistically (e.g. when the editor opens) — never block UI.</summary>
    public async Task FetchAsync(string exe, CancellationToken ct)
    {
        // Check-then-act on ContainsKey is a tolerated rare double-fetch window: the UI fires this once per
        // dialog-open (the second open is cache-warm). _cache is a ConcurrentDictionary, so the worst case is
        // one redundant call — never corruption.
        if (string.IsNullOrWhiteSpace(exe) || !_isConfigured() || _cache.ContainsKey(exe)) return;
        try
        {
            var lines = await _fetch(exe, ct);
            _cache[exe] = lines ?? Array.Empty<EvidenceLine>();
        }
        catch (OperationCanceledException) { /* caller cancelled — leave cache empty */ }
        catch { _cache[exe] = Array.Empty<EvidenceLine>(); /* fail-safe: external is optional */ }
    }

    // ── Real Anthropic web_search call (structured output) ──────────────────────────────
    private async Task<IReadOnlyList<EvidenceLine>> FetchViaAnthropic(IApiKeyStore keyStore, string exe, CancellationToken ct)
    {
        var key = keyStore.GetKey();
        if (string.IsNullOrWhiteSpace(key)) return Array.Empty<EvidenceLine>();
        // Per-call client (and its HttpClient) is acceptable here: the result is cached per exe, so this
        // path runs at most once per distinct app per process lifetime — not a hot/socket-churning loop.
        var client = new AnthropicClient { ApiKey = key };

        var prompt =
            $"Find verificerbare, kilde-citerede fakta om PC-spillet/appen med eksekverbar '{exe}' " +
            "der påvirker en skærm/GPU-profil: frame-rate-cap-adfærd, NVIDIA Reflex-understøttelse, " +
            "HDR/Dolby Vision-understøttelse, og kendte grafik-gotchas (fx borderless arver OS-refresh). " +
            "Brug web-søgning. Returnér KUN et JSON-objekt med formen " +
            "{\"facts\":[{\"label\":\"...\",\"value\":\"...\",\"source\":\"...\",\"url\":\"...\"}]} " +
            "— maks 5 fakta, hver med en kilde-URL. Ingen prosa udenfor JSON.";

        var parameters = new MessageCreateParams
        {
            Model = _model,
            MaxTokens = 1500,
            // Verified against the installed Anthropic SDK 12.24.1 (Rule #1, 2026-06-14): the non-beta
            // server tool is Anthropic.Models.Messages.WebSearchTool20260209 (parameterless ctor) and is a
            // member of Anthropic.Models.Messages.ToolUnion, so it implicit-converts into the Tools list —
            // the same list shape ClaudeClient.BuildTools produces.
            Tools = new List<ToolUnion> { new WebSearchTool20260209() },
            Messages = new List<MessageParam>
            {
                new() { Role = Role.User, Content = new List<ContentBlockParam> { new TextBlockParam { Text = prompt } } },
            },
        };

        // The server-side tool runs an internal loop; the response may carry stop_reason "pause_turn"
        // (resume). For v1 we do a single Create call and parse the final text block(s). (Resume loop = follow-up.)
        var resp = await client.Messages.Create(parameters, ct);

        // Message.Content is IReadOnlyList<ContentBlock>; each block's .Value is the concrete block object.
        var text = string.Concat(resp.Content
            .Select(b => b.Value)
            .OfType<TextBlock>()
            .Select(t => t.Text));

        return ParseFacts(text);
    }

    /// <summary>Parse the model's JSON {facts:[{label,value,source,url}]} into External evidence lines.
    /// Tolerant: extracts the first {...} block; returns empty on any malformed output / no facts.</summary>
    internal static IReadOnlyList<EvidenceLine> ParseFacts(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<EvidenceLine>();
        // IndexOf(char, StringComparison) exists; LastIndexOf has no char+StringComparison overload, so use
        // the string overload (same index for a single char) — both satisfy CA1307 (ordinal, culture-agnostic).
        int s = text.IndexOf('{', StringComparison.Ordinal), e = text.LastIndexOf("}", StringComparison.Ordinal);
        if (s < 0 || e <= s) return Array.Empty<EvidenceLine>();
        try
        {
            using var doc = JsonDocument.Parse(text.Substring(s, e - s + 1));
            if (!doc.RootElement.TryGetProperty("facts", out var facts) || facts.ValueKind != JsonValueKind.Array)
                return Array.Empty<EvidenceLine>();
            var list = new List<EvidenceLine>();
            foreach (var f in facts.EnumerateArray())
            {
                string Get(string k) => f.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                var label = Get("label"); var value = Get("value");
                if (label.Length == 0 || value.Length == 0) continue;
                var url = Get("url");
                list.Add(new EvidenceLine(label, value, Get("source"), ConfidenceTier.External, url.Length > 0 ? url : null));
            }
            return list;
        }
        catch (JsonException) { return Array.Empty<EvidenceLine>(); }
    }
}
