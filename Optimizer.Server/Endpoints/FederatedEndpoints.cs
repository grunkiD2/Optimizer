using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Optimizer.Server.Models;
using Optimizer.Server.Services;

namespace Optimizer.Server.Endpoints;

// ── Request / Response DTOs ─────────────────────────────────────────────────

public record FederatedContributeRequest(IReadOnlyList<FederatedContributionDto> Contributions);

public record FederatedContributionDto(
    string Category,
    double AcceptanceRate,
    int SampleWeight);

public record FederatedBaselineDto(
    string Category,
    double CommunityAcceptanceRate,
    int ContributorCount);

public record FederatedBaselinesResponse(IReadOnlyList<FederatedBaselineDto> Baselines);

// ── Endpoint registration ────────────────────────────────────────────────────

public static class FederatedEndpoints
{
    public static void MapFederated(this WebApplication app)
    {
        var group = app.MapGroup("/api/federated")
            .WithTags("FederatedLearning")
            .RequireAuthorization();

        // POST /api/federated/contribute
        // Accepts the current user's DP-noised per-category statistics.
        // The client MUST apply differential privacy noise before calling this endpoint.
        // The server validates bounds but does NOT apply additional noise.
        group.MapPost("/contribute", async (
            [FromBody] FederatedContributeRequest req,
            IFederatedLearningService fl,
            HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            if (userId == null) return Results.Unauthorized();

            if (req.Contributions == null || req.Contributions.Count == 0)
                return Results.BadRequest(new ApiError("invalid_request", "No contributions provided."));

            var contributions = req.Contributions
                .Select(c => new CategoryContribution(c.Category, c.AcceptanceRate, c.SampleWeight))
                .ToList();

            try
            {
                await fl.SubmitAsync(userId.Value, contributions);
                return Results.Ok(new { message = "Contributions recorded." });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new ApiError("invalid_contribution", ex.Message));
            }
        }).WithName("FederatedContribute");

        // GET /api/federated/baselines
        // Returns community-aggregated baselines.
        // Only categories meeting the minimum-contributor k-anonymity threshold are included.
        group.MapGet("/baselines", async (
            IFederatedLearningService fl,
            HttpContext ctx) =>
        {
            var userId = GetUserId(ctx);
            if (userId == null) return Results.Unauthorized();

            var baselines = await fl.GetBaselinesAsync();
            var dtos = baselines
                .Select(b => new FederatedBaselineDto(b.Category, b.CommunityAcceptanceRate, b.ContributorCount))
                .ToList();

            return Results.Ok(new FederatedBaselinesResponse(dtos));
        }).WithName("FederatedBaselines");
    }

    private static Guid? GetUserId(HttpContext ctx)
    {
        var sub = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? ctx.User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : null;
    }
}
