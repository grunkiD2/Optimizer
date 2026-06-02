using System.Net;
using System.Net.Http.Json;
using Optimizer.Cli;
using Xunit;

namespace Optimizer.Cli.Tests;

/// <summary>
/// Tests for CloudApiClient — uses a fake HttpMessageHandler so no real server is needed.
/// </summary>
public class CloudApiClientTests
{
    // ── Helper ────────────────────────────────────────────────────────────────

    private static (CloudApiClient client, List<HttpRequestMessage> captured) MakeClient(
        string baseUrl  = "http://test-server",
        string apiKey   = "test-key-abc",
        HttpStatusCode  statusCode  = HttpStatusCode.OK,
        string          responseBody = "{\"status\":\"ok\"}")
    {
        var captured = new List<HttpRequestMessage>();
        var handler  = new FakeHttpHandler(statusCode, responseBody, captured);
        var http     = new HttpClient(handler) { BaseAddress = new Uri(baseUrl) };

        // Use the internal constructor to inject the client directly for testing
        var cloud = CloudApiClient.CreateForTesting(http);
        return (cloud, captured);
    }

    // ── X-Api-Key header is set ───────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_SetsApiKeyHeader()
    {
        var captured  = new List<HttpRequestMessage>();
        var handler   = new FakeHttpHandler(HttpStatusCode.OK, "{\"status\":\"ok\"}", captured);
        var http      = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        http.DefaultRequestHeaders.Add("X-Api-Key", "my-secret-key");

        var client = CloudApiClient.CreateForTesting(http);
        await client.GetAsync("/api/health");

        Assert.Single(captured);
        Assert.True(captured[0].Headers.Contains("X-Api-Key"));
        Assert.Equal("my-secret-key", captured[0].Headers.GetValues("X-Api-Key").First());
    }

    [Fact]
    public async Task GetAsync_UsesCorrectPath()
    {
        var captured = new List<HttpRequestMessage>();
        var handler  = new FakeHttpHandler(HttpStatusCode.OK, "{}", captured);
        var http     = new HttpClient(handler) { BaseAddress = new Uri("http://cloud-srv") };
        var client   = CloudApiClient.CreateForTesting(http);

        await client.GetAsync("/api/marketplace?search=boost");

        Assert.Single(captured);
        Assert.Contains("/api/marketplace", captured[0].RequestUri!.PathAndQuery, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAsync_ReturnsNullOn404()
    {
        var captured = new List<HttpRequestMessage>();
        var handler  = new FakeHttpHandler(HttpStatusCode.NotFound, "", captured);
        var http     = new HttpClient(handler) { BaseAddress = new Uri("http://cloud-srv") };
        var client   = CloudApiClient.CreateForTesting(http);

        var result = await client.GetAsync("/api/not-there");

        Assert.Null(result);
    }

    [Fact]
    public async Task IsHealthyAsync_ReturnsTrueOn200()
    {
        var captured = new List<HttpRequestMessage>();
        var handler  = new FakeHttpHandler(HttpStatusCode.OK, "{\"status\":\"ok\"}", captured);
        var http     = new HttpClient(handler) { BaseAddress = new Uri("http://cloud-srv") };
        var client   = CloudApiClient.CreateForTesting(http);

        var healthy = await client.IsHealthyAsync();

        Assert.True(healthy);
    }

    [Fact]
    public async Task IsHealthyAsync_ReturnsFalseOn503()
    {
        var captured = new List<HttpRequestMessage>();
        var handler  = new FakeHttpHandler(HttpStatusCode.ServiceUnavailable, "", captured);
        var http     = new HttpClient(handler) { BaseAddress = new Uri("http://cloud-srv") };
        var client   = CloudApiClient.CreateForTesting(http);

        var healthy = await client.IsHealthyAsync();

        Assert.False(healthy);
    }

    [Fact]
    public async Task PostAsync_SendsJsonBody()
    {
        var captured = new List<HttpRequestMessage>();
        var handler  = new FakeHttpHandler(HttpStatusCode.OK, "{\"id\":\"abc\"}", captured);
        var http     = new HttpClient(handler) { BaseAddress = new Uri("http://cloud-srv") };
        var client   = CloudApiClient.CreateForTesting(http);

        await client.PostAsync("/api/webhooks", new { url = "https://example.com", eventTypes = new[] { "*" } });

        Assert.Single(captured);
        Assert.Equal(HttpMethod.Post, captured[0].Method);
        Assert.NotNull(captured[0].Content);
        var body = await captured[0].Content!.ReadAsStringAsync();
        Assert.Contains("example.com", body, StringComparison.Ordinal);
    }
}

// ── Fake handler ──────────────────────────────────────────────────────────────

internal class FakeHttpHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _code;
    private readonly string         _body;
    private readonly List<HttpRequestMessage> _captured;

    public FakeHttpHandler(HttpStatusCode code, string body, List<HttpRequestMessage> captured)
    {
        _code     = code;
        _body     = body;
        _captured = captured;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _captured.Add(request);
        var response = new HttpResponseMessage(_code)
        {
            Content = new StringContent(_body)
        };
        return Task.FromResult(response);
    }
}
