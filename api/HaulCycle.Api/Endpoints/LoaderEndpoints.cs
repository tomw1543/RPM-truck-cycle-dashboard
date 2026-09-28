using HaulCycle.Api.Data;
using HaulCycle.Api.Kpi;

namespace HaulCycle.Api.Endpoints;

public sealed record LoaderShiftData(
    DateOnly ShiftDate,
    string ShiftName,
    bool IsComplete,
    string LoaderName,
    int Trucks,
    decimal AvgQueueMin,
    decimal LoadingMin,
    decimal StoppedMin,
    decimal Utilisation,
    decimal MatchFactor);

public sealed record LoaderSummaryData(string LoaderName, decimal AvgUtilisation, decimal AvgQueueMin, decimal AvgMatchFactor);

public sealed record LoadersData(IReadOnlyList<LoaderShiftData> Shifts, IReadOnlyList<LoaderSummaryData> Summary);

/// <summary>/api/loaders: one row per loader per shift in the window - trucks assigned, match
/// factor, average queue, utilisation, stopped minutes - plus a per-loader window summary.
/// See LoaderShiftCalculator for the arithmetic (matches OptimisedLoaderStats' definitions).</summary>
public static class LoaderEndpoints
{
    public static void MapLoaderEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/loaders").CacheOutput("DataEndpoints");

        group.MapGet("", async Task<IResult> (
            DateOnly? from,
            DateOnly? to,
            string? shift,
            HaulCycleQueries queries,
            CancellationToken ct) =>
        {
            var meta = await queries.GetMetaAsync(ct);
            if (meta.AsOf is null)
                return Results.Ok(new Envelope<LoadersData>(null, from, to, new LoadersData([], [])));

            if (!RequestWindow.TryResolve(from, to, shift, meta.AsOf.Value, out var fromDt, out var toDt, out var fromDate, out var toDate, out var problem))
                return problem!;

            var cycles = await queries.GetCyclesForShiftWindowAsync(fromDate, toDate, shift, ct);
            var schedules = await queries.GetSchedulesAsync(fromDate, toDate, shift, ct);
            var loaderDelays = await queries.GetLoaderDelaysAsync(fromDt, toDt, ct);
            var loaderNames = await queries.GetLoaderNamesAsync(ct);

            var result = LoaderShiftCalculator.Calculate(cycles, schedules, loaderDelays, loaderNames, meta.AsOf.Value);

            var data = new LoadersData(
                result.Shifts.Select(MapShift).ToList(),
                result.Summary.Select(MapSummary).ToList());

            return Results.Ok(new Envelope<LoadersData>(meta.AsOf, fromDate, toDate, data));
        })
        .WithName("GetLoaders")
        .WithSummary("Per-loader, per-shift trucks, match factor, average queue, utilisation and stopped minutes, for a date window.");
    }

    private static LoaderShiftData MapShift(LoaderShiftCalculator.LoaderShiftResult r) =>
        new(r.ShiftDate, r.ShiftName, r.IsComplete, r.LoaderName, r.Trucks, r.AvgQueueMin, r.LoadingMin, r.StoppedMin, r.Utilisation, r.MatchFactor);

    private static LoaderSummaryData MapSummary(LoaderShiftCalculator.LoaderSummary s) =>
        new(s.LoaderName, s.AvgUtilisation, s.AvgQueueMin, s.AvgMatchFactor);
}
