using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Optimizer.Server.Data;
using Optimizer.Server.Models;
using Optimizer.Server.Services;
using Xunit;

namespace Optimizer.Server.Tests;

/// <summary>
/// Integration tests for API key management endpoints and API key authentication.
/// Each test creates its own isolated factory so rate-limiters and DB state don't bleed across.
/// </summary>
public class ApiKeyEndpointsTests
{
    // ── Factory helpers ────────────────────────────────────────────────────

    private static ApiKeyTestFactory CreateFactory() => new ApiKeyTestFactory();

    private static async Task<(System.Net.Http.HttpClient client, string accessToken, ApiKeyTestFactory factory)>
        GetAuthenticatedClientAsync()
    {
        var factory = CreateFactory();
        var client = factory.CreateClient();
        var emailCapture = factory.EmailCapture;

        var req1 = await client.PostAsJsonAsync("/api/auth/request-magic-link",
            new { email = "apikey-test@example.com", deviceName = "Test" });
        req1.EnsureSuccessStatusCode();

        var token = emailCapture.LastToken;
        Assert.NotNull(token);

        var verifyResp = await client.PostAsJsonAsync("/api/auth/verify", new { token });
        verifyResp.EnsureSuccessStatusCode();

        var auth = await verifyResp.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", auth.AccessToken);

        return (client, auth.AccessToken, factory);
    }

    // ── POST /api/keys ────────────────────────────────────────────────────

    [Fact]
    public async Task PostKeys_WithoutJwt_Returns401()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        var resp = await client.PostAsJsonAsync("/api/keys",
            new { name = "CI key", scopes = new[] { "sync:read" } });

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task PostKeys_WithJwt_Returns200AndRawKey()
    {
        var (client, _, factory) = await GetAuthenticatedClientAsync();
        using (factory)
        {
            var resp = await client.PostAsJsonAsync("/api/keys",
                new { name = "My CI key", scopes = new[] { "sync:read", "sync:write" } });

            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var body = await resp.Content.ReadFromJsonAsync<CreatedApiKeyDto>();
            Assert.NotNull(body);
            Assert.StartsWith("opt_live_", body.RawKey, StringComparison.Ordinal);
            Assert.Equal("My CI key", body.Name);
        }
    }

    // ── GET /api/scopes ───────────────────────────────────────────────────

    [Fact]
    public async Task GetScopes_Anonymous_ReturnsScopeList()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/api/scopes");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var scopes = await resp.Content.ReadFromJsonAsync<string[]>();
        Assert.NotNull(scopes);
        Assert.Contains("sync:read", scopes);
        Assert.Contains("plugins:manage", scopes);
    }

    // ── Authenticate with X-Api-Key ────────────────────────────────────────

    [Fact]
    public async Task XApiKey_ValidKey_AuthenticatesAndReturns200()
    {
        // Create a key via JWT
        var (jwtClient, _, factory) = await GetAuthenticatedClientAsync();
        using (factory)
        {
            var createResp = await jwtClient.PostAsJsonAsync("/api/keys",
                new { name = "SDK key", scopes = new[] { "sync:read" } });
            createResp.EnsureSuccessStatusCode();
            var created = await createResp.Content.ReadFromJsonAsync<CreatedApiKeyDto>();
            Assert.NotNull(created);

            // Use the raw key on a different (unauthenticated) client to the same factory
            var apiClient = factory.CreateClient();
            apiClient.DefaultRequestHeaders.Add("X-Api-Key", created.RawKey);

            var syncResp = await apiClient.GetAsync("/api/sync?since=0");

            Assert.Equal(HttpStatusCode.OK, syncResp.StatusCode);
        }
    }

    [Fact]
    public async Task XApiKey_LackingRequiredScope_Returns403()
    {
        // Create a key with only marketplace:read — try to hit sync:read endpoint
        var (jwtClient, _, factory) = await GetAuthenticatedClientAsync();
        using (factory)
        {
            var createResp = await jwtClient.PostAsJsonAsync("/api/keys",
                new { name = "Limited key", scopes = new[] { "marketplace:read" } });
            createResp.EnsureSuccessStatusCode();
            var created = await createResp.Content.ReadFromJsonAsync<CreatedApiKeyDto>();
            Assert.NotNull(created);

            var apiClient = factory.CreateClient();
            apiClient.DefaultRequestHeaders.Add("X-Api-Key", created.RawKey);

            // /api/sync GET requires scope:sync:read — this key doesn't have it
            var syncResp = await apiClient.GetAsync("/api/sync?since=0");

            Assert.Equal(HttpStatusCode.Forbidden, syncResp.StatusCode);
        }
    }

    [Fact]
    public async Task XApiKey_WithRequiredScope_Returns200()
    {
        var (jwtClient, _, factory) = await GetAuthenticatedClientAsync();
        using (factory)
        {
            var createResp = await jwtClient.PostAsJsonAsync("/api/keys",
                new { name = "Sync key", scopes = new[] { "sync:read", "sync:write" } });
            createResp.EnsureSuccessStatusCode();
            var created = await createResp.Content.ReadFromJsonAsync<CreatedApiKeyDto>();
            Assert.NotNull(created);

            var apiClient = factory.CreateClient();
            apiClient.DefaultRequestHeaders.Add("X-Api-Key", created.RawKey);

            var syncResp = await apiClient.GetAsync("/api/sync?since=0");

            Assert.Equal(HttpStatusCode.OK, syncResp.StatusCode);
        }
    }

    [Fact]
    public async Task XApiKey_RevokedKey_Returns401()
    {
        var (jwtClient, _, factory) = await GetAuthenticatedClientAsync();
        using (factory)
        {
            // Create
            var createResp = await jwtClient.PostAsJsonAsync("/api/keys",
                new { name = "Revokable", scopes = new[] { "sync:read" } });
            var created = await createResp.Content.ReadFromJsonAsync<CreatedApiKeyDto>();
            Assert.NotNull(created);

            // Revoke
            var deleteResp = await jwtClient.DeleteAsync($"/api/keys/{created.Id}");
            Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

            // Attempt to use the revoked key
            var apiClient = factory.CreateClient();
            apiClient.DefaultRequestHeaders.Add("X-Api-Key", created.RawKey);
            var syncResp = await apiClient.GetAsync("/api/sync?since=0");

            Assert.Equal(HttpStatusCode.Unauthorized, syncResp.StatusCode);
        }
    }

    // ── Rate limiting ──────────────────────────────────────────────────────

    [Fact]
    public async Task RateLimit_ExceedingLimit_Returns429()
    {
        // The factory sets PermitPerMinute=3 — 4th request must get 429.
        using var factory = new ApiKeyTestFactory(permitPerMinute: 3);
        var client = factory.CreateClient();

        HttpResponseMessage? lastResponse = null;
        for (int i = 0; i < 4; i++)
        {
            lastResponse = await client.GetAsync("/api/scopes");
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, lastResponse!.StatusCode);
    }

    // ── DTOs for deserialization ───────────────────────────────────────────

    private record CreatedApiKeyDto(Guid Id, string Name, string Prefix, string[] Scopes, string RawKey);

    // ── Test factory ───────────────────────────────────────────────────────

    public class ApiKeyTestFactory : WebApplicationFactory<Program>
    {
        private readonly int _permitPerMinute;
        private readonly int _authPermitPerMinute;

        public AuthEndpointsTests.CaptureEmailService EmailCapture { get; } = new();

        public ApiKeyTestFactory(int permitPerMinute = 100, int authPermitPerMinute = 100)
        {
            _permitPerMinute = permitPerMinute;
            _authPermitPerMinute = authPermitPerMinute;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                // Replace SQLite with an isolated in-memory DB
                var toRemove = services
                    .Where(d =>
                        d.ServiceType == typeof(OptimizerDbContext) ||
                        d.ServiceType == typeof(DbContextOptions<OptimizerDbContext>) ||
                        (d.ServiceType.IsGenericType &&
                         d.ServiceType.GetGenericTypeDefinition() ==
                             typeof(Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsConfiguration<>) &&
                         d.ServiceType.GenericTypeArguments[0] == typeof(OptimizerDbContext)))
                    .ToList();
                foreach (var d in toRemove) services.Remove(d);

                var dbName = "apikey-integration-" + Guid.NewGuid();
                services.AddDbContext<OptimizerDbContext>(opt =>
                    opt.UseInMemoryDatabase(dbName));

                // Replace email service
                services.RemoveAll<IEmailService>();
                services.AddSingleton<IEmailService>(EmailCapture);
            });

            builder.UseSetting("RateLimit:PermitPerMinute", _permitPerMinute.ToString());
            builder.UseSetting("RateLimit:AuthPermitPerMinute", _authPermitPerMinute.ToString());
            builder.UseEnvironment("Testing");
        }
    }
}
