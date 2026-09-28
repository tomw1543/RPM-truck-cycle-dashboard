using HaulCycle.Api.Data;
using HaulCycle.Api.Kpi;

namespace HaulCycle.Api.Endpoints;

// Response types for GET /api/loaders/shifts/{shiftDate}/{shiftName}
public sealed record LoaderArrivalData(string TruckName, DateTime ArrivalTime, decimal QueueMin);
public sealed record LoaderDelayWindowData(DateTime Start, DateTime End, decimal RateFactor);
public sealed record LoaderQueueData(string LoaderName, IReadOnlyList<LoaderArrivalData> Arrivals, IReadOnlyList<LoaderDelayWindowData> Delays);
public sealed record LoaderShiftQueueData(IReadOnlyList<LoaderQueueData> Loaders);

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
    /// <summary>Groups cycles and loader delays by loader name into the per-shift queue chart
    /// response. Extracted as a static method so it can be unit-tested without a WebApplication.</summary>
    public static LoaderShiftQueueData BuildLoaderShiftQueueData(
        IReadOnlyList<CycleRow> cycles,
        IReadOnlyList<LoaderDelayRow> loaderDelays,
        IReadOnlyList<string> loaderNames)
    {
        var loaders = loaderNames
            .OrderBy(n => n)
            .Select(loaderName =>
            {
                var arrivals = cycles
                    .Where(c => c.LoaderName == loaderName)
                    .OrderBy(c => c.StartTime)
                    .Select(c => new LoaderArrivalData(c.TruckName, c.StartTime, c.QueueMin))
                    .ToList();

                var delays = loaderDelays
                    .Where(d => d.LoaderName == loaderName)
                    .OrderBy(d => d.StartTime)
                    .Select(d => new LoaderDelayWindowData(d.StartTime, d.EndTime, d.RateFactor))
                    .ToList();

                return new LoaderQueueData(loaderName, arrivals, delays);
            })
            .ToList();

        return new LoaderShiftQueueData(loaders);
    }

    public static void MapLoaderEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/loaders").CacheOutput("DataEndpoints");

        app.MapGet("/api/loaders/shifts/{shiftDate}/{shiftName}", async Task<IResult> (
            string shiftDate,
            string shiftName,
            HaulCycleQueries queries,
            CancellationToken ct) =>
        {
            if (!DateOnly.TryParse(shiftDate, out var date))
                return TypedResults.Problem(
                    title: "Invalid shift date",
                    detail: "shiftDate must be a date in YYYY-MM-DD format.",
                    statusCode: StatusCodes.Status400BadRequest);

            if (shiftName != "Day" && shiftName != "Night")
                return TypedResults.Problem(
                    title: "Invalid shift name",
                    detail: "shiftName must be Day or Night.",
                    statusCode: StatusCodes.Status400BadRequest);

            var cycles = await queries.GetCyclesForShiftAsync(date, shiftName, ct);
            if (cycles.Count == 0)
                return TypedResults.Problem(
                    title: "Shift not found",
                    detail: $"No cycles found for {shiftDate} {shiftName}.",
                    statusCode: StatusCodes.Status404NotFound);

            var loaderDelays = await queries.GetLoaderDelaysForShiftAsync(date, shiftName, ct);
            var loaderNames = await queries.GetLoaderNamesAsync(ct);

            var data = BuildLoaderShiftQueueData(cycles, loaderDelays, loaderNames);
            return Results.Ok(data);
        })
        .WithName("GetLoaderShiftQueue")
        .WithSummary("Per-arrival queue times and loader delay windows for one shift, for the loader queue chart.");

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
