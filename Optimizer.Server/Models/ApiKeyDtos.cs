namespace Optimizer.Server.Models;

public record CreateApiKeyRequest(string Name, IReadOnlyList<string> Scopes, DateTime? ExpiresAtUtc);
