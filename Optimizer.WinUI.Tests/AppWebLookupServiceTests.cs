// AppWebLookupServiceTests.cs
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Optimizer.WinUI.Services.Intelligence;
using Xunit;

namespace Optimizer.WinUI.Tests;

public class AppWebLookupServiceTests
{
    [Fact]
    public void CachedFor_is_empty_before_any_fetch()
    {
        var svc = new AppWebLookupService(
            isConfigured: () => true,
            fetch: (_, _) => Task.FromResult<IReadOnlyList<EvidenceLine>>(Array.Empty<EvidenceLine>()),
            model: "claude-opus-4-8");
        Assert.Empty(svc.CachedFor("destiny2.exe"));
    }

    [Fact]
    public async Task FetchAsync_populates_cache_with_external_tier_lines_and_urls()
    {
        var fetched = new List<EvidenceLine>
        {
            new("Frame-cap", "menu 30 / spil ubundet", "RTSS/community", ConfidenceTier.External, "https://example.com/d2-fps"),
            new("Reflex", "understøttet", "NVIDIA-side", ConfidenceTier.External, "https://example.com/d2-reflex"),
        };
        var svc = new AppWebLookupService(
            isConfigured: () => true,
            fetch: (exe, ct) => Task.FromResult<IReadOnlyList<EvidenceLine>>(fetched),
            model: "claude-opus-4-8");

        await svc.FetchAsync("destiny2.exe", CancellationToken.None);

        var cached = svc.CachedFor("destiny2.exe");
        Assert.Equal(2, cached.Count);
        Assert.All(cached, l => Assert.Equal(ConfidenceTier.External, l.Tier));
        Assert.Contains(cached, l => l.SourceUrl == "https://example.com/d2-reflex");
    }

    [Fact]
    public async Task FetchAsync_is_noop_and_safe_when_not_configured()
    {
        bool fetchCalled = false;
        var svc = new AppWebLookupService(
            isConfigured: () => false,
            fetch: (_, _) => { fetchCalled = true; return Task.FromResult<IReadOnlyList<EvidenceLine>>(Array.Empty<EvidenceLine>()); },
            model: "claude-opus-4-8");

        await svc.FetchAsync("destiny2.exe", CancellationToken.None); // no key → must not call out
        Assert.False(fetchCalled);
        Assert.Empty(svc.CachedFor("destiny2.exe"));
    }

    [Fact]
    public async Task FetchAsync_swallows_fetch_errors_and_leaves_cache_empty()
    {
        var svc = new AppWebLookupService(
            isConfigured: () => true,
            fetch: (_, _) => throw new InvalidOperationException("network"),
            model: "claude-opus-4-8");
        await svc.FetchAsync("destiny2.exe", CancellationToken.None); // must not throw
        Assert.Empty(svc.CachedFor("destiny2.exe"));
    }

    [Fact]
    public void ParseFacts_parses_valid_json_and_returns_empty_on_malformed()
    {
        const string valid =
            "Her er fakta: {\"facts\":[" +
            "{\"label\":\"Frame-cap\",\"value\":\"menu 30 / spil ubundet\",\"source\":\"RTSS\",\"url\":\"https://example.com/fps\"}," +
            "{\"label\":\"Reflex\",\"value\":\"understøttet\",\"source\":\"NVIDIA\",\"url\":\"https://example.com/reflex\"}," +
            "{\"label\":\"\",\"value\":\"dropped — no label\",\"source\":\"x\",\"url\":\"https://example.com/x\"}" +
            "]} resten er prosa.";

        var lines = AppWebLookupService.ParseFacts(valid);
        Assert.Equal(2, lines.Count); // the blank-label entry is skipped
        Assert.All(lines, l => Assert.Equal(ConfidenceTier.External, l.Tier));
        Assert.Equal("Frame-cap", lines[0].Label);
        Assert.Equal("menu 30 / spil ubundet", lines[0].Value);
        Assert.Equal("RTSS", lines[0].Source);
        Assert.Equal("https://example.com/fps", lines[0].SourceUrl);
        Assert.Contains(lines, l => l.SourceUrl == "https://example.com/reflex");

        // Malformed / no-facts inputs all yield empty (fail-safe).
        Assert.Empty(AppWebLookupService.ParseFacts(""));
        Assert.Empty(AppWebLookupService.ParseFacts("no json here"));
        Assert.Empty(AppWebLookupService.ParseFacts("{ this is not valid json"));
        Assert.Empty(AppWebLookupService.ParseFacts("{\"other\":[1,2,3]}"));
        Assert.Empty(AppWebLookupService.ParseFacts("{\"facts\":[]}"));
    }
}
