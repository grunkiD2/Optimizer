using Microsoft.AspNetCore.Mvc;
using Optimizer.Server.Models;
using Optimizer.Server.Services;

namespace Optimizer.Server.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuth(this WebApplication app, int authPermitPerMinute = 10)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth")
            .RequireRateLimiting("auth");

        group.MapPost("/request-magic-link", async ([FromBody] RequestMagicLinkDto dto, IAuthService auth, HttpContext ctx) =>
        {
            var clientBase = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
            var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            await auth.RequestMagicLinkAsync(dto.Email, clientBase, ip);
            // Always return 202 to prevent email enumeration
            return Results.Accepted("/api/auth/check-email", new { message = "If the email is valid, a magic link has been sent." });
        }).WithName("RequestMagicLink").WithOpenApi();

        group.MapPost("/verify", async ([FromBody] VerifyMagicLinkDto dto, [FromQuery] string? device, IAuthService auth, HttpContext ctx) =>
        {
            var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var result = await auth.VerifyMagicLinkAsync(dto.Token, device ?? "Unknown Device", ip);
            if (result == null) return Results.BadRequest(new ApiError("invalid_token", "Token is invalid, expired, or already used."));
            return Results.Ok(result);
        }).WithName("VerifyMagicLink").WithOpenApi();

        group.MapPost("/refresh", async ([FromBody] RefreshTokenDto dto, IAuthService auth, HttpContext ctx) =>
        {
            var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var result = await auth.RefreshAsync(dto.RefreshToken, ip);
            if (result == null) return Results.Unauthorized();
            return Results.Ok(result);
        }).WithName("Refresh").WithOpenApi();

        group.MapPost("/logout", async ([FromBody] RefreshTokenDto dto, IAuthService auth) =>
        {
            await auth.RevokeSessionAsync(dto.RefreshToken);
            return Results.NoContent();
        }).WithName("Logout").WithOpenApi();
    }
}
