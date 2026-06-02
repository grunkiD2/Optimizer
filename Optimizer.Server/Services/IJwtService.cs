using Optimizer.Server.Data.Entities;

namespace Optimizer.Server.Services;

public interface IJwtService
{
    string IssueAccessToken(User user);
    (string token, string hash) IssueRefreshToken();  // raw token to client; hash to DB
    string Hash(string raw);
    DateTime AccessTokenExpiry { get; }
    DateTime RefreshTokenExpiry { get; }
}
