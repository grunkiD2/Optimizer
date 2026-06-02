namespace Optimizer.Server.Models;

public record RequestMagicLinkDto(string Email, string? DeviceName);
public record VerifyMagicLinkDto(string Token);
public record RefreshTokenDto(string RefreshToken);
public record AuthResponse(string AccessToken, string RefreshToken, DateTime ExpiresAtUtc, UserInfoDto User);
public record UserInfoDto(Guid Id, string Email, string DisplayName);
