using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Optimizer.Server.Models;
using Optimizer.Server.Services;

namespace Optimizer.Server.Endpoints;

public static class WebhookEndpoints
{
    public static void MapWebhooks(this WebApplication app)
    {
        // ── Webhook subscription management ──────────────────────────────────

        var webhooks = app.MapGroup("/api/webhooks").WithTags("Webhooks").RequireAuthorization();

        webhooks.MapPost("", async (
            [FromBody] CreateWebhookRequest req,
            IWebhookService svc,
            HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            if (userId == null) return Results.Unauthorized();

            try
            {
                var result = await svc.CreateAsync(userId.Value, req);
                return Results.Ok(result);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new ApiError("invalid_request", ex.Message));
            }
        }).WithName("CreateWebhook").WithOpenApi();

        webhooks.MapGet("", async (IWebhookService svc, HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            if (userId == null) return Results.Unauthorized();

            var list = await svc.ListAsync(userId.Value);
            return Results.Ok(list);
        }).WithName("ListWebhooks").WithOpenApi();

        webhooks.MapDelete("{id:guid}", async (
            Guid id,
            IWebhookService svc,
            HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            if (userId == null) return Results.Unauthorized();

            var deleted = await svc.DeleteAsync(userId.Value, id);
            return deleted ? Results.NoContent() : Results.NotFound();
        }).WithName("DeleteWebhook").WithOpenApi();

        // ── Event ingest ──────────────────────────────────────────────────────

        app.MapPost("/api/events", (
            [FromBody] IncomingEventDto evt,
            IWebhookService svc,
            HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            if (userId == null) return Results.Unauthorized();

            // Fire-and-forget: dispatch runs in background; caller gets 202 immediately
            _ = Task.Run(() => svc.DispatchAsync(userId.Value, evt));

            return Results.Accepted();
        }).WithTags("Events").RequireAuthorization()
          .WithName("IngestEvent").WithOpenApi();
    }

    private static Guid? GetUserId(HttpContext ctx)
    {
        var sub = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? ctx.User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : null;
    }
}
