using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Optimizer.Server.Data;
using Optimizer.Server.Data.Entities;
using Optimizer.Server.Services;
using Xunit;

namespace Optimizer.Server.Tests;

public class AuthServiceTests
{
    private static OptimizerDbContext CreateDb(string name)
    {
        var opts = new DbContextOptionsBuilder<OptimizerDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new OptimizerDbContext(opts);
    }

    private static JwtService CreateJwt()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = "test-secret-key-that-is-64-characters-long-for-hmac-sha256-signing",
                ["Jwt:Issuer"] = "test-issuer",
                ["Jwt:Audience"] = "test-audience",
                ["Jwt:AccessTokenMinutes"] = "30",
                ["Jwt:RefreshTokenDays"] = "30"
            })
            .Build();
        return new JwtService(config);
    }

    private static AuthService Create(OptimizerDbContext db, IEmailService? email = null)
    {
        var jwt = CreateJwt();
        email ??= new TestEmailCapture();
        return new AuthService(db, jwt, email);
    }

    [Fact]
    public async Task RequestMagicLink_StoresTokenInDb()
    {
        using var db = CreateDb(nameof(RequestMagicLink_StoresTokenInDb));
        var svc = Create(db);

        var result = await svc.RequestMagicLinkAsync("user@example.com", "http://localhost:3000", "127.0.0.1");

        Assert.True(result);
        var token = await db.MagicLinkTokens.FirstOrDefaultAsync();
        Assert.NotNull(token);
        Assert.Equal("user@example.com", token.Email);
        Assert.NotEmpty(token.TokenHash);
    }

    [Fact]
    public async Task RequestMagicLink_InvalidEmail_ReturnsFalse()
    {
        using var db = CreateDb(nameof(RequestMagicLink_InvalidEmail_ReturnsFalse));
        var svc = Create(db);

        var result = await svc.RequestMagicLinkAsync("not-an-email", "http://localhost:3000", "127.0.0.1");

        Assert.False(result);
        Assert.Equal(0, await db.MagicLinkTokens.CountAsync());
    }

    [Fact]
    public async Task RequestMagicLink_RateLimit_BlocksAfterThree()
    {
        using var db = CreateDb(nameof(RequestMagicLink_RateLimit_BlocksAfterThree));
        var svc = Create(db);

        for (int i = 0; i < 3; i++)
            await svc.RequestMagicLinkAsync("rate@example.com", "http://localhost:3000", "127.0.0.1");

        // 4th should fail
        var result = await svc.RequestMagicLinkAsync("rate@example.com", "http://localhost:3000", "127.0.0.1");
        Assert.False(result);
    }

    [Fact]
    public async Task VerifyMagicLink_ValidToken_CreatesUserAndReturnsAuth()
    {
        using var db = CreateDb(nameof(VerifyMagicLink_ValidToken_CreatesUserAndReturnsAuth));
        var capture = new TestEmailCapture();
        var svc = Create(db, capture);

        await svc.RequestMagicLinkAsync("new@example.com", "http://localhost:3000", "127.0.0.1");
        var rawToken = capture.LastToken!;

        var auth = await svc.VerifyMagicLinkAsync(rawToken, "Test Device", "127.0.0.1");

        Assert.NotNull(auth);
        Assert.NotEmpty(auth.AccessToken);
        Assert.NotEmpty(auth.RefreshToken);
        Assert.Equal("new@example.com", auth.User.Email);

        var user = await db.Users.FirstOrDefaultAsync();
        Assert.NotNull(user);
        Assert.Equal("new@example.com", user.Email);
    }

    [Fact]
    public async Task VerifyMagicLink_InvalidToken_ReturnsNull()
    {
        using var db = CreateDb(nameof(VerifyMagicLink_InvalidToken_ReturnsNull));
        var svc = Create(db);

        var auth = await svc.VerifyMagicLinkAsync("totally-fake-token", "Device", "127.0.0.1");

        Assert.Null(auth);
    }

    [Fact]
    public async Task VerifyMagicLink_AlreadyUsedToken_ReturnsNull()
    {
        using var db = CreateDb(nameof(VerifyMagicLink_AlreadyUsedToken_ReturnsNull));
        var capture = new TestEmailCapture();
        var svc = Create(db, capture);

        await svc.RequestMagicLinkAsync("reuse@example.com", "http://localhost:3000", "127.0.0.1");
        var rawToken = capture.LastToken!;

        // First use succeeds
        var first = await svc.VerifyMagicLinkAsync(rawToken, "Device", "127.0.0.1");
        Assert.NotNull(first);

        // Second use returns null
        var second = await svc.VerifyMagicLinkAsync(rawToken, "Device", "127.0.0.1");
        Assert.Null(second);
    }

    [Fact]
    public async Task VerifyMagicLink_ExpiredToken_ReturnsNull()
    {
        using var db = CreateDb(nameof(VerifyMagicLink_ExpiredToken_ReturnsNull));
        var jwt = CreateJwt();
        var capture = new TestEmailCapture();
        var svc = new AuthService(db, jwt, capture);

        // Manually insert an already-expired token
        var (rawToken, hash) = jwt.IssueRefreshToken();
        db.MagicLinkTokens.Add(new MagicLinkToken
        {
            Email = "expired@example.com",
            TokenHash = hash,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1),  // already expired
            IpAddress = "127.0.0.1"
        });
        await db.SaveChangesAsync();

        var auth = await svc.VerifyMagicLinkAsync(rawToken, "Device", "127.0.0.1");
        Assert.Null(auth);
    }

    [Fact]
    public async Task Refresh_RotatesRefreshToken()
    {
        using var db = CreateDb(nameof(Refresh_RotatesRefreshToken));
        var capture = new TestEmailCapture();
        var svc = Create(db, capture);

        await svc.RequestMagicLinkAsync("refresh@example.com", "http://localhost:3000", "127.0.0.1");
        var auth = await svc.VerifyMagicLinkAsync(capture.LastToken!, "Device", "127.0.0.1");
        var originalRefresh = auth!.RefreshToken;

        var refreshed = await svc.RefreshAsync(originalRefresh, "127.0.0.1");

        Assert.NotNull(refreshed);
        Assert.NotEqual(originalRefresh, refreshed.RefreshToken);
        Assert.Equal("refresh@example.com", refreshed.User.Email);
    }

    [Fact]
    public async Task Refresh_InvalidToken_ReturnsNull()
    {
        using var db = CreateDb(nameof(Refresh_InvalidToken_ReturnsNull));
        var svc = Create(db);

        var result = await svc.RefreshAsync("bad-refresh-token", "127.0.0.1");
        Assert.Null(result);
    }

    [Fact]
    public async Task RevokeSession_MarksSessionRevoked()
    {
        using var db = CreateDb(nameof(RevokeSession_MarksSessionRevoked));
        var capture = new TestEmailCapture();
        var svc = Create(db, capture);

        await svc.RequestMagicLinkAsync("revoke@example.com", "http://localhost:3000", "127.0.0.1");
        var auth = await svc.VerifyMagicLinkAsync(capture.LastToken!, "Device", "127.0.0.1");

        var revoked = await svc.RevokeSessionAsync(auth!.RefreshToken);
        Assert.True(revoked);

        var session = await db.UserSessions.FirstAsync();
        Assert.NotNull(session.RevokedAtUtc);

        // Refresh after revoke should fail
        var refreshResult = await svc.RefreshAsync(auth.RefreshToken, "127.0.0.1");
        Assert.Null(refreshResult);
    }

    // Captures the raw token from the magic link URL
    private class TestEmailCapture : IEmailService
    {
        public string? LastToken { get; private set; }

        public Task SendMagicLinkAsync(string toEmail, string magicLink)
        {
            // Extract token from URL like http://localhost:3000/auth/verify?token=XXX
            var uri = new Uri(magicLink);
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            LastToken = query["token"];
            return Task.CompletedTask;
        }
    }
}
