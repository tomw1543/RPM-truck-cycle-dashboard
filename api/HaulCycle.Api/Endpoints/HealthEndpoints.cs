namespace HaulCycle.Api.Endpoints;

public static class HealthEndpoints
{
    /// <summary>Liveness probe. Must never touch the database - Azure SQL auto-pauses
    /// when idle, and a probe hitting the DB would keep waking it (or block on the
    /// ~1 minute resume) for no reason.</summary>
    public static void MapHealthEndpoints(this WebApplication app)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
            .WithName("GetHealth")
            .WithSummary("Liveness probe. Does not touch the database.");
    }
}
