using System.Net;
using System.Net.Http.Json;
using System.Text;
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

public class AuthEndpointsTests : IClassFixture<AuthEndpointsTests.TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public AuthEndpointsTests(TestWebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_Returns200()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Health_ReturnsOkStatus()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/health");
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("ok", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Me_WithoutAuth_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RequestMagicLink_ValidEmail_Returns202()
    {
        var client = _factory.CreateClient();
        var payload = new { email = "integration@example.com", deviceName = "Test" };
        var response = await client.PostAsJsonAsync("/api/auth/request-magic-link", payload);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task RequestMagicLink_EmptyEmail_StillReturns202()
    {
        // Anti-enumeration: always 202 regardless
        var client = _factory.CreateClient();
        var payload = new { email = "", deviceName = "Test" };
        var response = await client.PostAsJsonAsync("/api/auth/request-magic-link", payload);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task Verify_InvalidToken_Returns400()
    {
        var client = _factory.CreateClient();
        var payload = new { token = "bad-fake-token-value" };
        var response = await client.PostAsJsonAsync("/api/auth/verify", payload);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Verify_InvalidToken_Returns400WithErrorCode()
    {
        var client = _factory.CreateClient();
        var payload = new { token = "another-bad-token" };
        var response = await client.PostAsJsonAsync("/api/auth/verify", payload);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("invalid_token", body, StringComparison.Ordinal);
    }

    public class TestWebAppFactory : WebApplicationFactory<Program>
    {
        public CaptureEmailService EmailCapture { get; } = new CaptureEmailService();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                // Remove ALL OptimizerDbContext-related registrations (including SQLite provider services)
                var toRemove = services
                    .Where(d =>
                        d.ServiceType == typeof(OptimizerDbContext) ||
                        d.ServiceType == typeof(DbContextOptions<OptimizerDbContext>) ||
                        (d.ServiceType.IsGenericType &&
                         d.ServiceType.GetGenericTypeDefinition() == typeof(IDbContextOptionsConfiguration<>) &&
                         d.ServiceType.GenericTypeArguments[0] == typeof(OptimizerDbContext)))
                    .ToList();
                foreach (var d in toRemove) services.Remove(d);

                var dbName = "integration-test-db-" + Guid.NewGuid().ToString();
                services.AddDbContext<OptimizerDbContext>(opt =>
                    opt.UseInMemoryDatabase(dbName));

                // Replace email service with capture service
                services.RemoveAll<IEmailService>();
                services.AddSingleton<IEmailService>(EmailCapture);
            });

            builder.UseEnvironment("Testing");
        }
    }

    public class CaptureEmailService : IEmailService
    {
        private string? _lastToken;

        public string? LastToken => _lastToken;

        public void Clear() => _lastToken = null;

        public Task SendMagicLinkAsync(string toEmail, string magicLink)
        {
            var uri = new Uri(magicLink);
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            _lastToken = query["token"];
            return Task.CompletedTask;
        }
    }
}

// Separate class so it gets its own factory and isolated database
public class AuthFlowIntegrationTests
{
    private static AuthEndpointsTests.TestWebAppFactory CreateFactory()
    {
        return new AuthEndpointsTests.TestWebAppFactory();
    }

    [Fact]
    public async Task FullAuthFlow_RequestVerifyRefreshLogout()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();
        var emailCapture = factory.EmailCapture;

        // 1. Request magic link
        var reqResponse = await client.PostAsJsonAsync("/api/auth/request-magic-link",
            new { email = "flow@example.com", deviceName = "Integration Test" });
        Assert.Equal(HttpStatusCode.Accepted, reqResponse.StatusCode);

        // 2. Verify magic link
        var token = emailCapture.LastToken;
        Assert.NotNull(token);

        var verifyResponse = await client.PostAsJsonAsync("/api/auth/verify", new { token });
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);

        var auth = await verifyResponse.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);
        Assert.NotEmpty(auth.AccessToken);
        Assert.NotEmpty(auth.RefreshToken);
        Assert.Equal("flow@example.com", auth.User.Email);

        // 3. Access /api/me with the token
        var meClient = factory.CreateClient();
        meClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var meResponse = await meClient.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);

        // 4. Refresh
        var refreshResponse = await client.PostAsJsonAsync("/api/auth/refresh",
            new { refreshToken = auth.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        var refreshed = await refreshResponse.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(refreshed);
        Assert.NotEqual(auth.RefreshToken, refreshed.RefreshToken);

        // 5. Logout
        var logoutResponse = await client.PostAsJsonAsync("/api/auth/logout",
            new { refreshToken = refreshed.RefreshToken });
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        // 6. Refresh after logout should fail
        var afterLogout = await client.PostAsJsonAsync("/api/auth/refresh",
            new { refreshToken = refreshed.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
    }
}
