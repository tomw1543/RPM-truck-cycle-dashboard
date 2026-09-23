using HaulCycle.Api.Data;

namespace HaulCycle.Api.Endpoints;

public sealed record MetaData(int Trucks, int Loaders, int Routes, int Destinations);

public static class MetaEndpoints
{
    public static void MapMetaEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/meta").CacheOutput("DataEndpoints");

        group.MapGet("", async (HaulCycleQueries queries, CancellationToken ct) =>
        {
            var meta = await queries.GetMetaAsync(ct);

            var data = new MetaData(meta.Counts.Trucks, meta.Counts.Loaders, meta.Counts.Routes, meta.Counts.Destinations);
            return Results.Ok(new Envelope<MetaData>(meta.AsOf, meta.FirstDate, meta.LastDate, data));
        })
        .WithName("GetMeta")
        .WithSummary("As-of time, data coverage dates, and fleet reference counts.");
    }
}
