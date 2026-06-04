using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Optimizer.Server.Models;
using Optimizer.Server.Services;

namespace Optimizer.Server.Endpoints;

public static class MarketplaceEndpoints
{
    public static void MapMarketplace(this WebApplication app)
    {
        var anonymousGroup = app.MapGroup("/api/marketplace").WithTags("Marketplace");
        var authGroup = app.MapGroup("/api/marketplace").WithTags("Marketplace").RequireAuthorization();

        anonymousGroup.MapGet("", async (
            [FromQuery] string? category,
            [FromQuery] string? search,
            [FromQuery] string? sort,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            IMarketplaceService svc) =>
        {
            var resp = await svc.BrowseAsync(category, search, sort ?? "downloads", page > 0 ? page.Value : 1, pageSize > 0 ? pageSize.Value : 20);
            return Results.Ok(resp);
        }).WithName("BrowseMarketplace");

        anonymousGroup.MapGet("/{publicId}", async (string publicId, IMarketplaceService svc) =>
        {
            var listing = await svc.GetByPublicIdAsync(publicId);
            return listing == null ? Results.NotFound() : Results.Ok(listing);
        }).WithName("GetMarketplaceListing");

        anonymousGroup.MapPost("/{publicId}/download", async (string publicId, IMarketplaceService svc) =>
        {
            var ok = await svc.IncrementDownloadAsync(publicId);
            return ok ? Results.NoContent() : Results.NotFound();
        }).WithName("IncrementDownload");

        authGroup.MapPost("/submit", async ([FromBody] SubmitListingRequest req, IMarketplaceService svc, HttpContext ctx) =>
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
        }).WithName("SubmitListing");

        authGroup.MapPost("/{publicId}/rate", async (string publicId, [FromBody] SubmitRatingRequest req, IMarketplaceService svc, HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            if (userId == null) return Results.Unauthorized();
            try
            {
                var rating = await svc.SubmitRatingAsync(publicId, userId.Value, req);
                return rating == null ? Results.NotFound() : Results.Ok(rating);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new ApiError("invalid_rating", ex.Message));
            }
        }).WithName("RateListing");

        authGroup.MapPost("/{publicId}/report", async (string publicId, [FromBody] ReportListingRequest req, IMarketplaceService svc, HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            if (userId == null) return Results.Unauthorized();
            var ok = await svc.ReportAsync(publicId, userId.Value, req);
            return ok ? Results.NoContent() : Results.NotFound();
        }).WithName("ReportListing");
    }

    private static Guid? GetUserId(HttpContext ctx)
    {
        var sub = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? ctx.User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : null;
    }
}
