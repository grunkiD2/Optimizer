using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Optimizer.Server.Data;
using Optimizer.Server.Data.Entities;

namespace Optimizer.Server.Services;

public class ApiKeyService : IApiKeyService
{
    private readonly OptimizerDbContext _db;

    // Throttle LastUsedAtUtc writes: only update if last-used is >1 min old.
    private static readonly TimeSpan LastUsedThrottle = TimeSpan.FromMinutes(1);

    public ApiKeyService(OptimizerDbContext db)
    {
        _db = db;
    }

    public async Task<CreatedApiKey> CreateAsync(Guid userId, string name, IReadOnlyList<string> scopes, DateTime? expiresAtUtc)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("API key name is required.", nameof(name));

        // Validate scopes
        var invalidScopes = scopes.Where(s => !ApiScopes.IsValid(s)).ToList();
        if (invalidScopes.Count > 0)
            throw new ArgumentException($"Unknown scope(s): {string.Join(", ", invalidScopes)}");

        if (scopes.Count == 0)
            throw new ArgumentException("At least one scope is required.", nameof(scopes));

        // Generate key: opt_live_{40 base64url chars} (random 30 bytes → 40 chars)
        var randomBytes = RandomNumberGenerator.GetBytes(30);
        var randomPart = Convert.ToBase64String(randomBytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        var rawKey = $"opt_live_{randomPart}";
        var prefix = $"opt_live_{randomPart[..4]}";
        var keyHash = Hash(rawKey);

        var entity = new ApiKey
        {
            UserId      = userId,
            Name        = name,
            Prefix      = prefix,
            KeyHash     = keyHash,
            ScopesCsv   = string.Join(",", scopes),
            ExpiresAtUtc = expiresAtUtc
        };

        _db.ApiKeys.Add(entity);
        await _db.SaveChangesAsync();

        return new CreatedApiKey(entity.Id, entity.Name, entity.Prefix, scopes, rawKey);
    }

    public async Task<IReadOnlyList<ApiKeyInfo>> ListAsync(Guid userId)
    {
        var keys = await _db.ApiKeys
            .Where(k => k.UserId == userId && k.RevokedAtUtc == null)
            .OrderByDescending(k => k.CreatedAtUtc)
            .ToListAsync();

        return keys.Select(k => new ApiKeyInfo(
            k.Id,
            k.Name,
            k.Prefix,
            k.ScopesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries),
            k.CreatedAtUtc,
            k.LastUsedAtUtc,
            k.ExpiresAtUtc))
            .ToList();
    }

    public async Task<bool> RevokeAsync(Guid userId, Guid keyId)
    {
        var key = await _db.ApiKeys.FirstOrDefaultAsync(k => k.Id == keyId && k.UserId == userId);
        if (key == null) return false;

        key.RevokedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<ApiKeyValidationResult?> ValidateAsync(string rawKey)
    {
        if (string.IsNullOrWhiteSpace(rawKey)) return null;

        var hash = Hash(rawKey);
        var key = await _db.ApiKeys.FirstOrDefaultAsync(k => k.KeyHash == hash);
        if (key == null) return null;
        if (!key.IsActive) return null;

        // Throttle LastUsedAtUtc writes to avoid a DB write on every request
        var now = DateTime.UtcNow;
        if (key.LastUsedAtUtc == null || now - key.LastUsedAtUtc.Value > LastUsedThrottle)
        {
            key.LastUsedAtUtc = now;
            await _db.SaveChangesAsync();
        }

        var scopes = key.ScopesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries);
        return new ApiKeyValidationResult(key.UserId, scopes);
    }

    private static string Hash(string raw)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes);
    }
}
