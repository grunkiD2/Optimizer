using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Optimizer.Server.Models;
using Optimizer.Server.Services;

namespace Optimizer.Server.Endpoints;

public static class PluginEndpoints
{
    public static void MapPlugins(this WebApplication app)
    {
        var anonymousGroup = app.MapGroup("/api/plugins").WithTags("Plugins");
        var authGroup      = app.MapGroup("/api/plugins").WithTags("Plugins").RequireAuthorization();

        anonymousGroup.MapGet("", async (
            [FromQuery] string? category,
            [FromQuery] string? search,
            [FromQuery] string? sort,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            IPluginMarketplaceService svc) =>
        {
            var resp = await svc.BrowseAsync(
                category, search, sort ?? "downloads",
                page > 0 ? page.Value : 1,
                pageSize > 0 ? pageSize.Value : 20);
            return Results.Ok(resp);
        }).WithName("BrowsePlugins");

        anonymousGroup.MapGet("/public-key", (IPluginSigningService signing) =>
        {
            return Results.Ok(new PublicKeyResponse(
                signing.PublicKeyBase64 ?? "",
                signing.IsConfigured));
        }).WithName("GetPluginPublicKey");

        anonymousGroup.MapGet("/{pluginId}", async (string pluginId, IPluginMarketplaceService svc) =>
        {
            var detail = await svc.GetByPluginIdAsync(pluginId);
            return detail == null ? Results.NotFound() : Results.Ok(detail);
        }).WithName("GetPlugin");

        anonymousGroup.MapPost("/{pluginId}/download", async (string pluginId, IPluginMarketplaceService svc) =>
        {
            var ok = await svc.IncrementDownloadAsync(pluginId);
            return ok ? Results.NoContent() : Results.NotFound();
        }).WithName("IncrementPluginDownload");

        authGroup.MapPost("/submit", async (
            [FromBody] SubmitPluginRequest req,
            IPluginMarketplaceService svc,
            HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            if (userId == null) return Results.Unauthorized();
            try
            {
                var resp = await svc.SubmitAsync(userId.Value, req);
                return Results.Ok(resp);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new ApiError("invalid_submission", ex.Message));
            }
        }).WithName("SubmitPlugin")
          .RequireAuthorization($"scope:{ApiScopes.PluginsManage}");

        authGroup.MapPost("/{pluginId}/rate", async (
            string pluginId,
            [FromBody] SubmitRatingRequest req,
            IPluginMarketplaceService svc,
            HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            if (userId == null) return Results.Unauthorized();
            try
            {
                var rating = await svc.SubmitRatingAsync(pluginId, userId.Value, req);
                return rating == null ? Results.NotFound() : Results.Ok(rating);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new ApiError("invalid_rating", ex.Message));
            }
        }).WithName("RatePlugin");
    }

    private static Guid? GetUserId(HttpContext ctx)
    {
        var sub = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
               ?? ctx.User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : null;
    }
}
