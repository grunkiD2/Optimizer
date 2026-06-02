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
            IServiceScopeFactory scopeFactory,
            HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            if (userId == null) return Results.Unauthorized();

            // Fire-and-forget: dispatch runs in a fresh DI scope so it gets its own
            // DbContext and WebhookService, independent of the request scope that is
            // disposed as soon as this handler returns.
            //
            // Without this fix, the request-scoped WebhookService / DbContext would be
            // disposed when the HTTP response is sent, causing SaveChangesAsync inside
            // DeliverWithRetryAsync (which runs after retry delays) to hit an
            // ObjectDisposedException on the DbContext.
            _ = Task.Run(async () =>
            {
                using var scope = scopeFactory.CreateScope();
                var svc    = scope.ServiceProvider.GetRequiredService<IWebhookService>();
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
                try
                {
                    await svc.DispatchAsync(userId.Value, evt);
                }
                catch (Exception ex)
                {
                    // Log dispatch failures — previously these were silently swallowed
                    logger.LogError(ex,
                        "Webhook dispatch failed for user {UserId}, event type {EventType}.",
                        userId.Value, evt.Type);
                }
            });

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
