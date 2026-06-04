using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Optimizer.Server.Models;
using Optimizer.Server.Services;

namespace Optimizer.Server.Endpoints;

public static class SyncEndpoints
{
    public static void MapSync(this WebApplication app)
    {
        var group = app.MapGroup("/api/sync").WithTags("Sync").RequireAuthorization();

        group.MapGet("", async ([FromQuery] long since, ISyncService sync, HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            if (userId == null) return Results.Unauthorized();
            var resp = await sync.PullAsync(userId.Value, since);
            return Results.Ok(resp);
        }).WithName("SyncPull")
          .RequireAuthorization($"scope:{ApiScopes.SyncRead}");

        group.MapPost("", async ([FromBody] SyncPushRequest req, ISyncService sync, HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            if (userId == null) return Results.Unauthorized();
            try
            {
                var resp = await sync.PushAsync(userId.Value, req);
                return Results.Ok(resp);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ApiError("invalid_request", ex.Message));
            }
        }).WithName("SyncPush")
          .RequireAuthorization($"scope:{ApiScopes.SyncWrite}");
    }

    private static Guid? GetUserId(HttpContext ctx)
    {
        var sub = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? ctx.User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : null;
    }
}
