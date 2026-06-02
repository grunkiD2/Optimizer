using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Configuration;
using Optimizer.Server.Data.Entities;
using Optimizer.Server.Services;
using Xunit;

namespace Optimizer.Server.Tests;

public class JwtServiceTests
{
    private static JwtService CreateService()
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

    [Fact]
    public void IssueAccessToken_ReturnsValidJwt()
    {
        var svc = CreateService();
        var user = new User { Id = Guid.NewGuid(), Email = "test@example.com", DisplayName = "Test" };

        var token = svc.IssueAccessToken(user);

        Assert.NotEmpty(token);
        var handler = new JwtSecurityTokenHandler();
        Assert.True(handler.CanReadToken(token));
        var parsed = handler.ReadJwtToken(token);
        Assert.Equal("test@example.com", parsed.Claims.First(c => c.Type == "email").Value);
        Assert.Equal(user.Id.ToString(), parsed.Claims.First(c => c.Type == "sub").Value);
    }

    [Fact]
    public void IssueAccessToken_ContainsDisplayName()
    {
        var svc = CreateService();
        var user = new User { Id = Guid.NewGuid(), Email = "user@test.com", DisplayName = "Alice" };

        var token = svc.IssueAccessToken(user);
        var handler = new JwtSecurityTokenHandler();
        var parsed = handler.ReadJwtToken(token);

        Assert.Equal("Alice", parsed.Claims.First(c => c.Type == "displayName").Value);
    }

    [Fact]
    public void Hash_IsDeterministic()
    {
        var svc = CreateService();
        const string input = "my-secret-token";

        var h1 = svc.Hash(input);
        var h2 = svc.Hash(input);

        Assert.Equal(h1, h2);
        Assert.NotEmpty(h1);
    }

    [Fact]
    public void Hash_DifferentInputsProduceDifferentHashes()
    {
        var svc = CreateService();

        var h1 = svc.Hash("token-a");
        var h2 = svc.Hash("token-b");

        Assert.NotEqual(h1, h2);
    }

    [Fact]
    public void IssueRefreshToken_ReturnsUniqueTokens()
    {
        var svc = CreateService();

        var (t1, h1) = svc.IssueRefreshToken();
        var (t2, h2) = svc.IssueRefreshToken();

        Assert.NotEqual(t1, t2);
        Assert.NotEqual(h1, h2);
    }

    [Fact]
    public void IssueRefreshToken_HashMatchesToken()
    {
        var svc = CreateService();

        var (token, hash) = svc.IssueRefreshToken();

        Assert.Equal(hash, svc.Hash(token));
    }

    [Fact]
    public void AccessTokenExpiry_IsInFuture()
    {
        var svc = CreateService();
        Assert.True(svc.AccessTokenExpiry > DateTime.UtcNow);
    }

    [Fact]
    public void RefreshTokenExpiry_IsInFuture()
    {
        var svc = CreateService();
        Assert.True(svc.RefreshTokenExpiry > DateTime.UtcNow);
    }
}
