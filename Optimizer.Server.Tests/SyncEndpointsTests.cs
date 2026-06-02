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

/// <summary>Integration tests for the /api/sync endpoints via TestServer.</summary>
public class SyncEndpointsTests : IClassFixture<SyncEndpointsTests.SyncTestFactory>
{
    private readonly SyncTestFactory _factory;

    public SyncEndpointsTests(SyncTestFactory factory)
    {
        _factory = factory;
    }

    // ── Auth guard ─────────────────────────────────────────────────────────

    [Fact]
    public async Task SyncPull_WithoutAuth_Returns401()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/sync?since=0");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task SyncPush_WithoutAuth_Returns401()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/sync", new { items = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    // ── Full round-trip ────────────────────────────────────────────────────

    [Fact]
    public async Task PushThenPull_RoundTrip_Works()
    {
        using var factory = new SyncTestFactory();
        var authClient = await CreateAuthenticatedClientAsync(factory);

        // Initial pull — empty
        var emptyPull = await authClient.GetAsync("/api/sync?since=0");
        Assert.Equal(HttpStatusCode.OK, emptyPull.StatusCode);
        var emptyBody = await emptyPull.Content.ReadFromJsonAsync<SyncPullResponse>();
        Assert.NotNull(emptyBody);
        Assert.Empty(emptyBody.Items);

        // Push two snapshots
        var pushPayload = new
        {
            items = new[]
            {
                new { itemType = "snapshot", itemId = "snap-1", payload = "{\"name\":\"Gaming\"}", isDeleted = false },
                new { itemType = "snapshot", itemId = "snap-2", payload = "{\"name\":\"Work\"}", isDeleted = false },
            }
        };
        var pushResp = await authClient.PostAsJsonAsync("/api/sync", pushPayload);
        Assert.Equal(HttpStatusCode.OK, pushResp.StatusCode);
        var pushBody = await pushResp.Content.ReadFromJsonAsync<SyncPushResponse>();
        Assert.NotNull(pushBody);
        Assert.Equal(2, pushBody.Results.Count);
        Assert.Equal(2, pushBody.ServerVersion);

        // Pull — should return both items
        var pullResp = await authClient.GetAsync("/api/sync?since=0");
        Assert.Equal(HttpStatusCode.OK, pullResp.StatusCode);
        var pullBody = await pullResp.Content.ReadFromJsonAsync<SyncPullResponse>();
        Assert.NotNull(pullBody);
        Assert.Equal(2, pullBody.Items.Count);
        Assert.Equal(2, pullBody.Cursor);

        // Pull with cursor at latest — empty delta
        var deltaPull = await authClient.GetAsync($"/api/sync?since={pullBody.Cursor}");
        var deltaBody = await deltaPull.Content.ReadFromJsonAsync<SyncPullResponse>();
        Assert.NotNull(deltaBody);
        Assert.Empty(deltaBody.Items);
    }

    [Fact]
    public async Task Push_InvalidType_Returns400()
    {
        using var factory = new SyncTestFactory();
        var authClient = await CreateAuthenticatedClientAsync(factory);

        var pushPayload = new
        {
            items = new[]
            {
                new { itemType = "INVALID", itemId = "x", payload = "{}", isDeleted = false }
            }
        };
        var resp = await authClient.PostAsJsonAsync("/api/sync", pushPayload);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ── Helper: full magic-link auth flow to get a bearer token ────────────

    private static async Task<HttpClient> CreateAuthenticatedClientAsync(SyncTestFactory factory)
    {
        var client = factory.CreateClient();

        // Request magic link
        var mlResp = await client.PostAsJsonAsync("/api/auth/request-magic-link",
            new { email = "sync-test@example.com", deviceName = "SyncTest" });
        Assert.Equal(HttpStatusCode.Accepted, mlResp.StatusCode);

        var token = factory.EmailCapture.LastToken;
        Assert.NotNull(token);

        // Verify to obtain JWT
        var verifyResp = await client.PostAsJsonAsync("/api/auth/verify", new { token });
        Assert.Equal(HttpStatusCode.OK, verifyResp.StatusCode);
        var jsonOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var auth = await verifyResp.Content.ReadFromJsonAsync<AuthResponse>(jsonOpts);
        Assert.NotNull(auth);
        Assert.NotEmpty(auth.AccessToken);

        // Create an authenticated client
        var authClient = factory.CreateClient();
        authClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return authClient;
    }

    // ── Test factory ───────────────────────────────────────────────────────

    public class SyncTestFactory : WebApplicationFactory<Program>
    {
        public AuthEndpointsTests.CaptureEmailService EmailCapture { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                // Swap out the real DB for in-memory
                var toRemove = services
                    .Where(d =>
                        d.ServiceType == typeof(OptimizerDbContext) ||
                        d.ServiceType == typeof(DbContextOptions<OptimizerDbContext>) ||
                        (d.ServiceType.IsGenericType &&
                         d.ServiceType.GetGenericTypeDefinition() == typeof(IDbContextOptionsConfiguration<>) &&
                         d.ServiceType.GenericTypeArguments[0] == typeof(OptimizerDbContext)))
                    .ToList();
                foreach (var d in toRemove) services.Remove(d);

                var dbName = "sync-integration-" + Guid.NewGuid().ToString();
                services.AddDbContext<OptimizerDbContext>(opt =>
                    opt.UseInMemoryDatabase(dbName));

                services.RemoveAll<IEmailService>();
                services.AddSingleton<IEmailService>(EmailCapture);
            });

            builder.UseEnvironment("Testing");
        }
    }
}
