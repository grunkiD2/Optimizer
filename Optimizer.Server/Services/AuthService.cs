using Microsoft.EntityFrameworkCore;
using Optimizer.Server.Data;
using Optimizer.Server.Data.Entities;
using Optimizer.Server.Models;

namespace Optimizer.Server.Services;

public class AuthService : IAuthService
{
    private readonly OptimizerDbContext _db;
    private readonly IJwtService _jwt;
    private readonly IEmailService _email;

    private const int MagicLinkValidMinutes = 15;

    public AuthService(OptimizerDbContext db, IJwtService jwt, IEmailService email)
    {
        _db = db; _jwt = jwt; _email = email;
    }

    public async Task<bool> RequestMagicLinkAsync(string email, string clientBaseUrl, string ipAddress)
    {
        var normalized = email.Trim().ToLowerInvariant();
        if (!IsValidEmail(normalized)) return false;

        // Rate-limit: max 3 unused tokens per email
        var existingCount = await _db.MagicLinkTokens.CountAsync(t =>
            t.Email == normalized && t.UsedAtUtc == null && t.ExpiresAtUtc > DateTime.UtcNow);
        if (existingCount >= 3) return false;

        var (rawToken, hash) = _jwt.IssueRefreshToken();  // reuse the cryptographic random generator
        var magicLink = $"{clientBaseUrl.TrimEnd('/')}/auth/verify?token={Uri.EscapeDataString(rawToken)}";

        _db.MagicLinkTokens.Add(new MagicLinkToken
        {
            Email = normalized,
            TokenHash = hash,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(MagicLinkValidMinutes),
            IpAddress = ipAddress
        });
        await _db.SaveChangesAsync();

        await _email.SendMagicLinkAsync(normalized, magicLink);
        return true;
    }

    public async Task<AuthResponse?> VerifyMagicLinkAsync(string token, string deviceName, string ipAddress)
    {
        var hash = _jwt.Hash(token);
        var record = await _db.MagicLinkTokens
            .Where(t => t.TokenHash == hash && t.UsedAtUtc == null && t.ExpiresAtUtc > DateTime.UtcNow)
            .FirstOrDefaultAsync();
        if (record == null) return null;

        record.UsedAtUtc = DateTime.UtcNow;

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == record.Email);
        if (user == null)
        {
            user = new User { Email = record.Email, DisplayName = record.Email.Split('@')[0] };
            _db.Users.Add(user);
        }
        user.LastLoginAtUtc = DateTime.UtcNow;

        var (refreshRaw, refreshHash) = _jwt.IssueRefreshToken();
        var session = new UserSession
        {
            UserId = user.Id,
            RefreshTokenHash = refreshHash,
            ExpiresAtUtc = _jwt.RefreshTokenExpiry,
            DeviceName = deviceName,
            IpAddress = ipAddress
        };
        _db.UserSessions.Add(session);
        await _db.SaveChangesAsync();

        var accessToken = _jwt.IssueAccessToken(user);
        return new AuthResponse(
            accessToken,
            refreshRaw,
            _jwt.AccessTokenExpiry,
            new UserInfoDto(user.Id, user.Email, user.DisplayName));
    }

    public async Task<AuthResponse?> RefreshAsync(string refreshToken, string ipAddress)
    {
        var hash = _jwt.Hash(refreshToken);
        var session = await _db.UserSessions
            .Include(s => s.User)
            .Where(s => s.RefreshTokenHash == hash && s.RevokedAtUtc == null && s.ExpiresAtUtc > DateTime.UtcNow)
            .FirstOrDefaultAsync();
        if (session?.User == null) return null;

        // Rotate refresh token
        var (newRaw, newHash) = _jwt.IssueRefreshToken();
        session.RefreshTokenHash = newHash;
        session.ExpiresAtUtc = _jwt.RefreshTokenExpiry;
        session.IpAddress = ipAddress;
        await _db.SaveChangesAsync();

        var accessToken = _jwt.IssueAccessToken(session.User);
        return new AuthResponse(
            accessToken,
            newRaw,
            _jwt.AccessTokenExpiry,
            new UserInfoDto(session.User.Id, session.User.Email, session.User.DisplayName));
    }

    public async Task<bool> RevokeSessionAsync(string refreshToken)
    {
        var hash = _jwt.Hash(refreshToken);
        var session = await _db.UserSessions.FirstOrDefaultAsync(s =>
            s.RefreshTokenHash == hash && s.RevokedAtUtc == null);
        if (session == null) return false;
        session.RevokedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch { return false; }
    }
}
