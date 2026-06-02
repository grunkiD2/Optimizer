using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Optimizer.Server.Data;
using Optimizer.Server.Models;
using Optimizer.Server.Services;
using Xunit;

namespace Optimizer.Server.Tests;

/// <summary>Integration tests for the /api/webhooks and /api/events endpoints.</summary>
public class WebhookEndpointsTests : IClassFixture<WebhookEndpointsTests.WebhookTestFactory>
{
    private readonly WebhookTestFactory _factory;

    public WebhookEndpointsTests(WebhookTestFactory factory)
    {
        _factory = factory;
    }

    // Public IP literals used in integration tests to avoid DNS resolution.
    // Using IP literals means the SSRF check doesn't need to resolve hostnames — fast and hermetic.
    // 93.184.216.34  = example.com (IANA)
    // 93.184.216.35  = a second example.com IP used to differentiate test entries
    private const string TestHookUrl1 = "https://93.184.216.34/hook";
    private const string TestHookUrl2 = "https://93.184.216.35/hook";
    private const string TestHookUrl3 = "https://93.184.216.34/hook-delete";

    // ── Auth guard ─────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateWebhook_WithoutAuth_Returns401()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/webhooks",
            new { url = TestHookUrl1, eventTypes = (string[]?)null });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    // ── CRUD via authenticated client ──────────────────────────────────────

    [Fact]
    public async Task CreateWebhook_WithAuth_ReturnsSecretOnce()
    {
        using var factory = new WebhookTestFactory();
        var client = await CreateAuthenticatedClientAsync(factory);

        var resp = await client.PostAsJsonAsync("/api/webhooks",
            new { url = TestHookUrl1, eventTypes = (string[]?)null });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var jsonOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var body = await resp.Content.ReadFromJsonAsync<CreatedWebhookDto>(jsonOpts);
        Assert.NotNull(body);
        Assert.NotEqual(Guid.Empty, body!.Id);
        Assert.False(string.IsNullOrEmpty(body.Secret));
    }

    [Fact]
    public async Task ListWebhooks_ReturnsCreatedWebhook()
    {
        using var factory = new WebhookTestFactory();
        var client = await CreateAuthenticatedClientAsync(factory);

        // Create one
        await client.PostAsJsonAsync("/api/webhooks",
            new { url = TestHookUrl2, eventTypes = (string[]?)null });

        // List
        var resp = await client.GetAsync("/api/webhooks");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var jsonOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var list = await resp.Content.ReadFromJsonAsync<List<WebhookDto>>(jsonOpts);
        Assert.NotNull(list);
        Assert.Contains(list!, w => w.Url == TestHookUrl2);
    }

    [Fact]
    public async Task DeleteWebhook_RemovesIt()
    {
        using var factory = new WebhookTestFactory();
        var client = await CreateAuthenticatedClientAsync(factory);

        // Create — use IP literal so SSRF check passes without DNS
        var createResp = await client.PostAsJsonAsync("/api/webhooks",
            new { url = TestHookUrl3, eventTypes = (string[]?)null });
        Assert.Equal(HttpStatusCode.OK, createResp.StatusCode);
        var jsonOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var created = await createResp.Content.ReadFromJsonAsync<CreatedWebhookDto>(jsonOpts);
        Assert.NotNull(created);

        // Delete
        var delResp = await client.DeleteAsync($"/api/webhooks/{created!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delResp.StatusCode);

        // Verify gone from list
        var listResp = await client.GetAsync("/api/webhooks");
        var list = await listResp.Content.ReadFromJsonAsync<List<WebhookDto>>(jsonOpts);
        Assert.DoesNotContain(list!, w => w.Id == created.Id);
    }

    [Fact]
    public async Task PostEvent_Returns202()
    {
        using var factory = new WebhookTestFactory();
        var client = await CreateAuthenticatedClientAsync(factory);

        var resp = await client.PostAsJsonAsync("/api/events", new
        {
            type         = "OptimizationApplied",
            title        = "Opt applied",
            detail       = "Disable animations",
            timestampUtc = DateTime.UtcNow,
            data         = (object?)null
        });

        Assert.Equal(HttpStatusCode.Accepted, resp.StatusCode);
    }

    // ── Auth helper ────────────────────────────────────────────────────────

    private static async Task<HttpClient> CreateAuthenticatedClientAsync(WebhookTestFactory factory)
    {
        var client = factory.CreateClient();

        var mlResp = await client.PostAsJsonAsync("/api/auth/request-magic-link",
            new { email = "webhook-test@example.com", deviceName = "WebhookTest" });
        Assert.Equal(HttpStatusCode.Accepted, mlResp.StatusCode);

        var token = factory.EmailCapture.LastToken;
        Assert.NotNull(token);

        var verifyResp = await client.PostAsJsonAsync("/api/auth/verify", new { token });
        Assert.Equal(HttpStatusCode.OK, verifyResp.StatusCode);
        var jsonOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var auth = await verifyResp.Content.ReadFromJsonAsync<AuthResponse>(jsonOpts);
        Assert.NotNull(auth);
        Assert.NotEmpty(auth!.AccessToken);

        var authClient = factory.CreateClient();
        authClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return authClient;
    }

    // ── Test factory ───────────────────────────────────────────────────────

    public class WebhookTestFactory : WebApplicationFactory<Program>
    {
        public AuthEndpointsTests.CaptureEmailService EmailCapture { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                var toRemove = services
                    .Where(d =>
                        d.ServiceType == typeof(OptimizerDbContext) ||
                        d.ServiceType == typeof(DbContextOptions<OptimizerDbContext>) ||
                        (d.ServiceType.IsGenericType &&
                         d.ServiceType.GetGenericTypeDefinition() == typeof(IDbContextOptionsConfiguration<>) &&
                         d.ServiceType.GenericTypeArguments[0] == typeof(OptimizerDbContext)))
                    .ToList();
                foreach (var d in toRemove) services.Remove(d);

                var dbName = "webhook-integration-" + Guid.NewGuid();
                services.AddDbContext<OptimizerDbContext>(opt =>
                    opt.UseInMemoryDatabase(dbName));

                services.RemoveAll<IEmailService>();
                services.AddSingleton<IEmailService>(EmailCapture);
            });

            builder.UseEnvironment("Testing");
        }
    }
}
