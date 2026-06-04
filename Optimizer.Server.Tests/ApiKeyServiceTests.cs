using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Optimizer.Server.Data;
using Optimizer.Server.Data.Entities;
using Optimizer.Server.Services;
using Xunit;

namespace Optimizer.Server.Tests;

/// <summary>
/// Unit tests for ApiKeyService using an in-memory EF Core database.
/// </summary>
public class ApiKeyServiceTests
{
    private static OptimizerDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<OptimizerDbContext>()
            .UseInMemoryDatabase("apikey-unit-" + Guid.NewGuid())
            .Options;
        return new OptimizerDbContext(opts);
    }

    private static ApiKeyService CreateService(OptimizerDbContext db) => new ApiKeyService(db);

    private static User SeedUser(OptimizerDbContext db)
    {
        var user = new User { Email = "test@example.com", DisplayName = "Tester" };
        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }

    // ── Create ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_ReturnsRawKeyWithCorrectPrefix()
    {
        using var db = CreateDb();
        var svc = CreateService(db);
        var user = SeedUser(db);

        var result = await svc.CreateAsync(user.Id, "CI key", [ApiScopes.SyncRead], null);

        Assert.StartsWith("opt_live_", result.RawKey, StringComparison.Ordinal);
        Assert.StartsWith("opt_live_", result.Prefix, StringComparison.Ordinal);
        // Prefix contains first 4 chars of random part
        Assert.Equal("opt_live_" + result.RawKey["opt_live_".Length..][..4], result.Prefix);
    }

    [Fact]
    public async Task Create_StoresOnlyHash_NotRawKey()
    {
        using var db = CreateDb();
        var svc = CreateService(db);
        var user = SeedUser(db);

        var result = await svc.CreateAsync(user.Id, "Test", [ApiScopes.SyncRead], null);

        var stored = await db.ApiKeys.SingleAsync(k => k.Id == result.Id);
        Assert.DoesNotContain(result.RawKey, stored.KeyHash, StringComparison.Ordinal);
        Assert.NotEqual(result.RawKey, stored.KeyHash);
        // Hash should be a 64-char hex string (SHA-256)
        Assert.Equal(64, stored.KeyHash.Length);
        Assert.Matches("^[0-9A-F]+$", stored.KeyHash);
    }

    [Fact]
    public async Task Create_RejectsUnknownScope()
    {
        using var db = CreateDb();
        var svc = CreateService(db);
        var user = SeedUser(db);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.CreateAsync(user.Id, "Bad key", ["unknown:scope"], null));

        Assert.Contains("Unknown scope", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_RejectsEmptyScopes()
    {
        using var db = CreateDb();
        var svc = CreateService(db);
        var user = SeedUser(db);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.CreateAsync(user.Id, "No scopes", [], null));
    }

    [Fact]
    public async Task Create_StoresCorrectScopes()
    {
        using var db = CreateDb();
        var svc = CreateService(db);
        var user = SeedUser(db);
        var scopes = new[] { ApiScopes.SyncRead, ApiScopes.SyncWrite };

        var result = await svc.CreateAsync(user.Id, "Multi-scope", scopes, null);

        Assert.Equal(2, result.Scopes.Count);
        Assert.Contains(ApiScopes.SyncRead, result.Scopes);
        Assert.Contains(ApiScopes.SyncWrite, result.Scopes);
    }

    // ── Validate ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Validate_CorrectKey_ReturnsUserIdAndScopes()
    {
        using var db = CreateDb();
        var svc = CreateService(db);
        var user = SeedUser(db);
        var created = await svc.CreateAsync(user.Id, "Valid", [ApiScopes.SyncRead], null);

        var validation = await svc.ValidateAsync(created.RawKey);

        Assert.NotNull(validation);
        Assert.Equal(user.Id, validation.UserId);
        Assert.Contains(ApiScopes.SyncRead, validation.Scopes);
    }

    [Fact]
    public async Task Validate_WrongKey_ReturnsNull()
    {
        using var db = CreateDb();
        var svc = CreateService(db);
        var user = SeedUser(db);
        await svc.CreateAsync(user.Id, "Key", [ApiScopes.SyncRead], null);

        var result = await svc.ValidateAsync("opt_live_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");

        Assert.Null(result);
    }

    [Fact]
    public async Task Validate_RevokedKey_ReturnsNull()
    {
        using var db = CreateDb();
        var svc = CreateService(db);
        var user = SeedUser(db);
        var created = await svc.CreateAsync(user.Id, "Soon revoked", [ApiScopes.SyncRead], null);
        await svc.RevokeAsync(user.Id, created.Id);

        var result = await svc.ValidateAsync(created.RawKey);

        Assert.Null(result);
    }

    [Fact]
    public async Task Validate_ExpiredKey_ReturnsNull()
    {
        using var db = CreateDb();
        var svc = CreateService(db);
        var user = SeedUser(db);
        // Create a key that already expired 1 second ago
        var created = await svc.CreateAsync(user.Id, "Expired", [ApiScopes.SyncRead],
            expiresAtUtc: DateTime.UtcNow.AddSeconds(-1));

        var result = await svc.ValidateAsync(created.RawKey);

        Assert.Null(result);
    }

    // ── List ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_ReturnsUserKeys_WithoutSecrets()
    {
        using var db = CreateDb();
        var svc = CreateService(db);
        var user = SeedUser(db);
        await svc.CreateAsync(user.Id, "Key A", [ApiScopes.SyncRead], null);
        await svc.CreateAsync(user.Id, "Key B", [ApiScopes.MarketplaceRead], null);

        var list = await svc.ListAsync(user.Id);

        Assert.Equal(2, list.Count);
        // Verify no hash or raw key exposed — ApiKeyInfo has no such field
        Assert.All(list, k =>
        {
            Assert.NotEmpty(k.Name);
            Assert.NotEmpty(k.Prefix);
            Assert.StartsWith("opt_live_", k.Prefix, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task List_IsScopedToUser_UserBCannotSeeUserAKeys()
    {
        using var db = CreateDb();
        var svc = CreateService(db);
        var userA = SeedUser(db);
        var userB = new User { Email = "b@example.com", DisplayName = "B" };
        db.Users.Add(userB);
        await db.SaveChangesAsync();

        await svc.CreateAsync(userA.Id, "A's key", [ApiScopes.SyncRead], null);

        var listB = await svc.ListAsync(userB.Id);

        Assert.Empty(listB);
    }

    // ── Revoke ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Revoke_MarksKeyInactive()
    {
        using var db = CreateDb();
        var svc = CreateService(db);
        var user = SeedUser(db);
        var created = await svc.CreateAsync(user.Id, "To revoke", [ApiScopes.SyncRead], null);

        var revokeResult = await svc.RevokeAsync(user.Id, created.Id);

        Assert.True(revokeResult);
        var stored = await db.ApiKeys.SingleAsync(k => k.Id == created.Id);
        Assert.NotNull(stored.RevokedAtUtc);
        Assert.False(stored.IsActive);
    }
}
