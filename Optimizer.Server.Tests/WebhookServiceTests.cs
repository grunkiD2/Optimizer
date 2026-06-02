using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Optimizer.Server.Data;
using Optimizer.Server.Data.Entities;
using Optimizer.Server.Models;
using Optimizer.Server.Services;
using Xunit;

namespace Optimizer.Server.Tests;

/// <summary>
/// Unit tests for WebhookService using an in-memory DB and a stub HttpMessageHandler.
/// </summary>
public class WebhookServiceTests : IDisposable
{
    private readonly OptimizerDbContext _db;
    private readonly Guid _userId;
    private readonly StubHttpHandler _stubHttp;
    private readonly WebhookService _svc;

    public WebhookServiceTests()
    {
        var opts = new DbContextOptionsBuilder<OptimizerDbContext>()
            .UseInMemoryDatabase("webhook-unit-" + Guid.NewGuid())
            .Options;
        _db = new OptimizerDbContext(opts);

        _userId = Guid.NewGuid();
        _db.Users.Add(new User { Id = _userId, Email = "whtest@example.com" });
        _db.SaveChanges();

        _stubHttp = new StubHttpHandler();
        var http = new HttpClient(_stubHttp);
        _svc = new WebhookService(_db, http, NullLogger<WebhookService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_StoresSubscription_AndReturnsSecret()
    {
        var result = await _svc.CreateAsync(_userId, new CreateWebhookRequest("https://93.184.216.34/hook", null));

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("https://93.184.216.34/hook", result.Url);
        Assert.False(string.IsNullOrEmpty(result.Secret));

        var stored = await _db.WebhookSubscriptions.FindAsync(result.Id);
        Assert.NotNull(stored);
        Assert.Equal(result.Secret, stored.Secret); // secret stored plain
    }

    [Fact]
    public async Task Create_RejectsNonHttpUrl()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _svc.CreateAsync(_userId, new CreateWebhookRequest("ftp://example.com/hook", null)));
    }

    [Fact]
    public async Task Create_RejectsRelativeUrl()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _svc.CreateAsync(_userId, new CreateWebhookRequest("/relative/path", null)));
    }

    // ── List ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_ScopedToUser()
    {
        var otherId = Guid.NewGuid();
        _db.Users.Add(new User { Id = otherId, Email = "other@example.com" });
        _db.SaveChanges();

        await _svc.CreateAsync(_userId,   new CreateWebhookRequest("https://93.184.216.34/hook",  null));
        await _svc.CreateAsync(otherId,   new CreateWebhookRequest("https://93.184.216.35/hook", null));

        var list = await _svc.ListAsync(_userId);
        Assert.Single(list);
        Assert.Equal("https://93.184.216.34/hook", list[0].Url);
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_RemovesSubscription()
    {
        var created = await _svc.CreateAsync(_userId, new CreateWebhookRequest("https://93.184.216.34/hook", null));
        var deleted = await _svc.DeleteAsync(_userId, created.Id);

        Assert.True(deleted);
        var list = await _svc.ListAsync(_userId);
        Assert.Empty(list);
    }

    [Fact]
    public async Task Delete_WrongUser_ReturnsFalse()
    {
        var otherId = Guid.NewGuid();
        var created = await _svc.CreateAsync(_userId, new CreateWebhookRequest("https://93.184.216.34/hook", null));

        var deleted = await _svc.DeleteAsync(otherId, created.Id);
        Assert.False(deleted);
    }

    // ── Dispatch ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Dispatch_MatchesAllEventsSubscription()
    {
        _stubHttp.SetResponse(HttpStatusCode.OK);
        await _svc.CreateAsync(_userId, new CreateWebhookRequest("https://93.184.216.34/hook", null)); // all events

        await _svc.DispatchAsync(_userId, new IncomingEventDto(
            "OptimizationApplied", "Opt", "Detail", DateTime.UtcNow, null));

        Assert.Equal(1, _stubHttp.RequestCount);
    }

    [Fact]
    public async Task Dispatch_MatchesTypeFilteredSubscription()
    {
        _stubHttp.SetResponse(HttpStatusCode.OK);
        await _svc.CreateAsync(_userId, new CreateWebhookRequest(
            "https://93.184.216.34/hook",
            new[] { "OptimizationApplied" }));

        await _svc.DispatchAsync(_userId, new IncomingEventDto(
            "OptimizationApplied", "Opt", "Detail", DateTime.UtcNow, null));

        Assert.Equal(1, _stubHttp.RequestCount);
    }

    [Fact]
    public async Task Dispatch_SkipsNonMatchingType()
    {
        _stubHttp.SetResponse(HttpStatusCode.OK);
        await _svc.CreateAsync(_userId, new CreateWebhookRequest(
            "https://93.184.216.34/hook",
            new[] { "PluginInstalled" }));

        await _svc.DispatchAsync(_userId, new IncomingEventDto(
            "OptimizationApplied", "Opt", "Detail", DateTime.UtcNow, null));

        Assert.Equal(0, _stubHttp.RequestCount);
    }

    [Fact]
    public async Task Dispatch_SignsPayloadWithHmac()
    {
        _stubHttp.SetResponse(HttpStatusCode.OK);
        var created = await _svc.CreateAsync(_userId, new CreateWebhookRequest("https://93.184.216.34/hook", null));

        await _svc.DispatchAsync(_userId, new IncomingEventDto(
            "OptimizationApplied", "Opt", "Detail", DateTime.UtcNow, null));

        Assert.Equal(1, _stubHttp.RequestCount);
        Assert.NotNull(_stubHttp.LastHeaders);
        Assert.True(_stubHttp.LastHeaders!.TryGetValues("X-Optimizer-Signature", out var sigValues));
        var sigHeader = sigValues.First();
        Assert.StartsWith("sha256=", sigHeader, StringComparison.Ordinal);

        // Verify the HMAC is correct against the captured body bytes
        var bodyBytes = _stubHttp.LastBodyBytes;
        Assert.NotNull(bodyBytes);
        var expectedHash = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(created.Secret),
            bodyBytes!);
        var expectedHex = Convert.ToHexString(expectedHash).ToLowerInvariant();
        Assert.Equal($"sha256={expectedHex}", sigHeader);
    }

    [Fact]
    public async Task Dispatch_FailedDelivery_IncrementsConsecutiveFailures()
    {
        _stubHttp.SetResponse(HttpStatusCode.InternalServerError);
        var created = await _svc.CreateAsync(_userId, new CreateWebhookRequest("https://93.184.216.34/hook", null));

        await _svc.DispatchAsync(_userId, new IncomingEventDto(
            "OptimizationApplied", "Opt", "Detail", DateTime.UtcNow, null));

        var sub = await _db.WebhookSubscriptions.FindAsync(created.Id);
        Assert.NotNull(sub);
        Assert.True(sub!.ConsecutiveFailures > 0);
    }

    [Fact]
    public async Task Dispatch_AutoDisablesAfterThresholdFailures()
    {
        _stubHttp.SetResponse(HttpStatusCode.InternalServerError);
        var created = await _svc.CreateAsync(_userId, new CreateWebhookRequest("https://93.184.216.34/hook", null));

        // Force 10 failures (the auto-disable threshold)
        for (int i = 0; i < 10; i++)
        {
            // Re-enable manually between dispatches so each dispatch counts
            var s = await _db.WebhookSubscriptions.FindAsync(created.Id);
            s!.IsActive = true;
            await _db.SaveChangesAsync();

            await _svc.DispatchAsync(_userId, new IncomingEventDto(
                "OptimizationApplied", "Opt", "Detail", DateTime.UtcNow, null));
        }

        var sub = await _db.WebhookSubscriptions.FindAsync(created.Id);
        Assert.NotNull(sub);
        Assert.False(sub!.IsActive);
    }

    // ── Stub HTTP handler ─────────────────────────────────────────────────────

    private sealed class StubHttpHandler : HttpMessageHandler
    {
        private HttpStatusCode _statusCode = HttpStatusCode.OK;
        private volatile int _requestCount;

        public int RequestCount => _requestCount;
        public HttpRequestMessage? LastRequest { get; private set; }
        /// <summary>Body bytes captured eagerly before the content is disposed.</summary>
        public byte[]? LastBodyBytes { get; private set; }
        /// <summary>Request headers captured (a copy that remains accessible after disposal).</summary>
        public System.Net.Http.Headers.HttpRequestHeaders? LastHeaders { get; private set; }

        public void SetResponse(HttpStatusCode code) => _statusCode = code;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Capture body bytes eagerly — the content may be disposed after SendAsync returns
            LastBodyBytes = request.Content != null
                ? await request.Content.ReadAsByteArrayAsync(cancellationToken)
                : null;
            LastRequest = request;
            LastHeaders = request.Headers;
            Interlocked.Increment(ref _requestCount);
            return new HttpResponseMessage(_statusCode);
        }
    }
}
