using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Optimizer.Server.Models;
using Optimizer.Server.Services;

namespace Optimizer.Server.Endpoints;

public static class ApiKeyEndpoints
{
    public static void MapApiKeys(this WebApplication app)
    {
        // Discovery endpoint — anonymous
        app.MapGet("/api/scopes", () =>
            Results.Ok(ApiScopes.All))
            .WithTags("ApiKeys")
            .WithName("ListScopes");

        // JWT-only group — you cannot mint keys with a key
        var jwtGroup = app.MapGroup("/api/keys")
            .WithTags("ApiKeys")
            .RequireAuthorization(JwtBearerDefaults.AuthenticationScheme);

        jwtGroup.MapPost("", async (
            [FromBody] CreateApiKeyRequest req,
            IApiKeyService apiKeyService,
            HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            if (userId == null) return Results.Unauthorized();

            try
            {
                var created = await apiKeyService.CreateAsync(userId.Value, req.Name, req.Scopes, req.ExpiresAtUtc);
                return Results.Ok(created);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new ApiError("invalid_request", ex.Message));
            }
        }).WithName("CreateApiKey");

        jwtGroup.MapGet("", async (IApiKeyService apiKeyService, HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            if (userId == null) return Results.Unauthorized();

            var keys = await apiKeyService.ListAsync(userId.Value);
            return Results.Ok(keys);
        }).WithName("ListApiKeys");

        jwtGroup.MapDelete("/{id:guid}", async (Guid id, IApiKeyService apiKeyService, HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            if (userId == null) return Results.Unauthorized();

            var revoked = await apiKeyService.RevokeAsync(userId.Value, id);
            return revoked ? Results.NoContent() : Results.NotFound();
        }).WithName("RevokeApiKey");
    }

    private static Guid? GetUserId(HttpContext ctx)
    {
        var sub = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
               ?? ctx.User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : null;
    }
}
