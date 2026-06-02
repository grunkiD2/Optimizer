using Optimizer.Server.Data.Entities;

namespace Optimizer.Server.Services;

public record CreatedApiKey(Guid Id, string Name, string Prefix, IReadOnlyList<string> Scopes, string RawKey);
public record ApiKeyInfo(Guid Id, string Name, string Prefix, IReadOnlyList<string> Scopes, DateTime CreatedAtUtc, DateTime? LastUsedAtUtc, DateTime? ExpiresAtUtc);

public interface IApiKeyService
{
    Task<CreatedApiKey> CreateAsync(Guid userId, string name, IReadOnlyList<string> scopes, DateTime? expiresAtUtc);
    Task<IReadOnlyList<ApiKeyInfo>> ListAsync(Guid userId);
    Task<bool> RevokeAsync(Guid userId, Guid keyId);
    /// <summary>Validation path (used by auth handler): returns (userId, scopes) if valid, else null.</summary>
    Task<ApiKeyValidationResult?> ValidateAsync(string rawKey);
}

public record ApiKeyValidationResult(Guid UserId, IReadOnlyList<string> Scopes);
