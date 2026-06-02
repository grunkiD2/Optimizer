namespace Optimizer.Server.Endpoints;

public static class HealthEndpoints
{
    public static void MapHealth(this WebApplication app)
    {
        app.MapGet("/api/health", () => Results.Ok(new { status = "ok", version = "1.0", time = DateTime.UtcNow }))
           .WithName("Health").WithTags("System").WithOpenApi();
    }
}
