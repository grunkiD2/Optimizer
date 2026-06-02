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
using Optimizer.Server.Data.Entities;
using Optimizer.Server.Models;
using Optimizer.Server.Services;
using Xunit;

namespace Optimizer.Server.Tests;

public class PluginEndpointsTests
{
    private static readonly string ValidManifestYaml =
        "manifest_version: 1\nid: endpoint-test-plugin\nname: Endpoint Test\ndescription: Test.\ncategory: Privacy\nchanges:\n  - type: registry\n    path: HKCU\\Test\n    value: X\n    value_type: dword\n    apply: \"1\"\n    revert: \"0\"";

    // ── Anonymous browse ──────────────────────────────────────────────────────

    [Fact]
    public async Task Browse_WithoutAuth_Returns200()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/api/plugins");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Browse_ReturnsSeededListings()
    {
        using var factory = CreateFactory();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OptimizerDbContext>();
            db.PluginListings.Add(new PluginListing
            {
                PluginId = "browse-seed", Name = "Browse Seed",
                Category = "Privacy", ManifestYaml = ValidManifestYaml,
                ManifestSha256 = "abc", Status = ListingStatus.Approved
            });
            await db.SaveChangesAsync();
        }

        var client = factory.CreateClient();
        var resp   = await client.GetAsync("/api/plugins");
        var body   = await resp.Content.ReadFromJsonAsync<PluginBrowseResponse>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(body);
        Assert.True(body!.Total >= 1);
    }

    [Fact]
    public async Task GetPlugin_Nonexistent_Returns404()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/api/plugins/no-such-plugin");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task GetPlugin_Existing_Returns200_WithManifestAndSignature()
    {
        using var factory = CreateFactory();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OptimizerDbContext>();
            db.PluginListings.Add(new PluginListing
            {
                PluginId = "get-test-plugin", Name = "Get Test",
                Category = "Privacy", ManifestYaml = ValidManifestYaml,
                ManifestSha256 = "abc", Signature = "SIG==",
                Status = ListingStatus.Approved, Verified = true
            });
            await db.SaveChangesAsync();
        }

        var client = factory.CreateClient();
        var resp   = await client.GetAsync("/api/plugins/get-test-plugin");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<PluginDetailDto>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(body);
        Assert.Equal("get-test-plugin", body!.PluginId);
        Assert.Equal(ValidManifestYaml, body.ManifestYaml);
        Assert.Equal("SIG==", body.Signature);
    }

    [Fact]
    public async Task PublicKey_Returns200_WithKey()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/api/plugins/public-key");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<PublicKeyResponse>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(body);
        Assert.NotEmpty(body!.PublicKey);
    }

    // ── Auth-gated endpoints ──────────────────────────────────────────────────

    [Fact]
    public async Task Submit_WithoutAuth_Returns401()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        var req  = new SubmitPluginRequest(ValidManifestYaml);
        var resp = await client.PostAsJsonAsync("/api/plugins/submit", req);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Rate_WithoutAuth_Returns401()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        var resp = await client.PostAsJsonAsync("/api/plugins/some-plugin/rate",
            new SubmitRatingRequest(4, null));
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    // ── Authenticated submit ──────────────────────────────────────────────────

    [Fact]
    public async Task Submit_WithAuth_Returns200AndPending()
    {
        using var factory     = CreateFactory();
        var authClient = await CreateAuthenticatedClientAsync(factory);

        var req  = new SubmitPluginRequest(ValidManifestYaml);
        var resp = await authClient.PostAsJsonAsync("/api/plugins/submit", req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<SubmitPluginResponse>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(body);
        Assert.Equal(ListingStatusDto.Pending, body!.Status);
    }

    [Fact]
    public async Task Submit_WithEmptyManifest_Returns400()
    {
        using var factory     = CreateFactory();
        var authClient = await CreateAuthenticatedClientAsync(factory);

        var req  = new SubmitPluginRequest("");
        var resp = await authClient.PostAsJsonAsync("/api/plugins/submit", req);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ── IncrementDownload ─────────────────────────────────────────────────────

    [Fact]
    public async Task IncrementDownload_Existing_Returns204()
    {
        using var factory = CreateFactory();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OptimizerDbContext>();
            db.PluginListings.Add(new PluginListing
            {
                PluginId = "dl-endpoint-plugin", Name = "DL Test",
                Category = "Privacy", ManifestYaml = ValidManifestYaml,
                ManifestSha256 = "abc", Status = ListingStatus.Approved
            });
            await db.SaveChangesAsync();
        }

        var client = factory.CreateClient();
        var resp   = await client.PostAsync("/api/plugins/dl-endpoint-plugin/download", null);
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static PluginTestFactory CreateFactory() => new();

    private static async Task<HttpClient> CreateAuthenticatedClientAsync(PluginTestFactory factory)
    {
        var client = factory.CreateClient();

        var mlResp = await client.PostAsJsonAsync("/api/auth/request-magic-link",
            new { email = "plugin-test@example.com", deviceName = "PluginTest" });
        Assert.Equal(HttpStatusCode.Accepted, mlResp.StatusCode);

        var token = factory.EmailCapture.LastToken;
        Assert.NotNull(token);

        var verifyResp = await client.PostAsJsonAsync("/api/auth/verify", new { token });
        Assert.Equal(HttpStatusCode.OK, verifyResp.StatusCode);
        var auth = await verifyResp.Content.ReadFromJsonAsync<AuthResponse>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(auth);

        var authClient = factory.CreateClient();
        authClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", auth!.AccessToken);
        return authClient;
    }

    // ── Test factory ──────────────────────────────────────────────────────────

    public class PluginTestFactory : WebApplicationFactory<Program>
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

                var dbName = "plugin-integration-" + Guid.NewGuid();
                services.AddDbContext<OptimizerDbContext>(opt => opt.UseInMemoryDatabase(dbName));

                services.RemoveAll<IEmailService>();
                services.AddSingleton<IEmailService>(EmailCapture);
            });

            builder.UseEnvironment("Testing");
        }
    }
}
