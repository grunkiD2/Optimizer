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

/// <summary>Integration tests for /api/marketplace endpoints via TestServer.</summary>
public class MarketplaceEndpointsTests
{
    // ── Anonymous browse ──────────────────────────────────────────────────────

    [Fact]
    public async Task Browse_WithoutAuth_Returns200()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/api/marketplace");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Browse_ReturnsSeededListings()
    {
        using var factory = CreateFactory();
        // Seed a listing directly
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OptimizerDbContext>();
            db.MarketplaceListings.Add(new MarketplaceListing
            {
                PublicId = "test-seed", Name = "Test Seed", Category = "Gaming",
                TagsJson = "[]", OptimizationsJson = "[]", Status = ListingStatus.Approved
            });
            await db.SaveChangesAsync();
        }

        var client = factory.CreateClient();
        var resp = await client.GetAsync("/api/marketplace");
        var body = await resp.Content.ReadFromJsonAsync<MarketplaceBrowseResponse>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(body);
        Assert.True(body!.Total >= 1);
    }

    [Fact]
    public async Task GetListing_NonExistent_Returns404()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/api/marketplace/nonexistent-id");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task GetListing_Existing_Returns200()
    {
        using var factory = CreateFactory();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OptimizerDbContext>();
            db.MarketplaceListings.Add(new MarketplaceListing
            {
                PublicId = "get-test", Name = "Get Test", Category = "Gaming",
                TagsJson = "[]", OptimizationsJson = "[]", Status = ListingStatus.Approved
            });
            await db.SaveChangesAsync();
        }

        var client = factory.CreateClient();
        var resp = await client.GetAsync("/api/marketplace/get-test");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    // ── Auth-gated endpoints ──────────────────────────────────────────────────

    [Fact]
    public async Task Submit_WithoutAuth_Returns401()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        var req = new SubmitListingRequest("Name", "Desc", "Gaming", ["tag"], ["DisableBackgroundApps"]);
        var resp = await client.PostAsJsonAsync("/api/marketplace/submit", req);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Rate_WithoutAuth_Returns401()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        var resp = await client.PostAsJsonAsync("/api/marketplace/some-id/rate",
            new SubmitRatingRequest(4, null));
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Report_WithoutAuth_Returns401()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        var resp = await client.PostAsJsonAsync("/api/marketplace/some-id/report",
            new ReportListingRequest("Spam", null));
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    // ── Authenticated submit ──────────────────────────────────────────────────

    [Fact]
    public async Task Submit_WithAuth_Returns200AndPending()
    {
        using var factory = CreateFactory();
        var authClient = await CreateAuthenticatedClientAsync(factory);

        var req = new SubmitListingRequest("My Awesome Profile", "A great config", "Gaming", ["fps"], ["DisableBackgroundApps"]);
        var resp = await authClient.PostAsJsonAsync("/api/marketplace/submit", req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<SubmitListingResponse>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(body);
        Assert.Equal(ListingStatusDto.Pending, body!.Status);
    }

    [Fact]
    public async Task Submit_WithInvalidData_Returns400()
    {
        using var factory = CreateFactory();
        var authClient = await CreateAuthenticatedClientAsync(factory);

        // Empty name should fail validation
        var req = new SubmitListingRequest("", "Desc", "Gaming", [], ["opt"]);
        var resp = await authClient.PostAsJsonAsync("/api/marketplace/submit", req);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ── Full submit → approve → browse flow ──────────────────────────────────

    [Fact]
    public async Task SubmitApproveAndBrowse_Flow()
    {
        using var factory = CreateFactory();
        var authClient = await CreateAuthenticatedClientAsync(factory);

        // 1. Submit
        var req = new SubmitListingRequest("Flow Test Profile", "Flow test desc", "Productivity", ["focus"], ["DisableAnimations"]);
        var submitResp = await authClient.PostAsJsonAsync("/api/marketplace/submit", req);
        Assert.Equal(HttpStatusCode.OK, submitResp.StatusCode);
        var submitted = await submitResp.Content.ReadFromJsonAsync<SubmitListingResponse>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(submitted);

        // 2. Manually approve in DB (simulate moderation)
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OptimizerDbContext>();
            var listing = await db.MarketplaceListings.FindAsync(submitted!.Id);
            Assert.NotNull(listing);
            listing!.Status = ListingStatus.Approved;
            await db.SaveChangesAsync();
        }

        // 3. Browse should now include the approved listing
        var client = factory.CreateClient();
        var browseResp = await client.GetAsync("/api/marketplace?sort=newest");
        var browse = await browseResp.Content.ReadFromJsonAsync<MarketplaceBrowseResponse>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(browse);
        Assert.True(browse!.Listings.Any(l => l.Name == "Flow Test Profile"));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static MarketplaceTestFactory CreateFactory() => new();

    private static async Task<HttpClient> CreateAuthenticatedClientAsync(MarketplaceTestFactory factory)
    {
        var client = factory.CreateClient();

        var mlResp = await client.PostAsJsonAsync("/api/auth/request-magic-link",
            new { email = "mkt-test@example.com", deviceName = "MktTest" });
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

    public class MarketplaceTestFactory : WebApplicationFactory<Program>
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

                var dbName = "mkt-integration-" + Guid.NewGuid();
                services.AddDbContext<OptimizerDbContext>(opt => opt.UseInMemoryDatabase(dbName));

                services.RemoveAll<IEmailService>();
                services.AddSingleton<IEmailService>(EmailCapture);
            });

            builder.UseEnvironment("Testing");
        }
    }
}
