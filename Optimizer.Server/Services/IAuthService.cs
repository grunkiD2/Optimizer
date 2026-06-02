using Optimizer.Server.Models;

namespace Optimizer.Server.Services;

public interface IAuthService
{
    Task<bool> RequestMagicLinkAsync(string email, string clientBaseUrl, string ipAddress);
    Task<AuthResponse?> VerifyMagicLinkAsync(string token, string deviceName, string ipAddress);
    Task<AuthResponse?> RefreshAsync(string refreshToken, string ipAddress);
    Task<bool> RevokeSessionAsync(string refreshToken);
}
