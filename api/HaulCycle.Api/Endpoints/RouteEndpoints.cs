using HaulCycle.Api.Data;
using HaulCycle.Api.Kpi;

namespace HaulCycle.Api.Endpoints;

/// <summary>One phase's window average alongside its all-time benchmark (see
/// RouteBenchmarkCalculator).</summary>
public sealed record RoutePhaseData(decimal? AverageMin, decimal? BenchmarkMin, decimal BookMin);

public sealed record RoutePhasesData(
    RoutePhaseData Queue,
    RoutePhaseData Load,
    RoutePhaseData Haul,
    RoutePhaseData Dump,
    RoutePhaseData Return);

/// <summary>One row per route (all 9, including routes with zero cycles in the window).
/// vsBook is a fraction (averageCycleMin / bookCycleMin - 1); null when the route had no cycles
/// in the window. benchmarkCycleMin is the route's all-time 25th-percentile total cycle time,
/// independent of the requested window.</summary>
public sealed record RouteData(
    string RouteName,
    string LoaderName,
    string DestinationName,
    string Material,
    decimal DistanceKm,
    decimal GradePercent,
    decimal BookCycleMin,
    int Cycles,
    decimal Tonnes,
    decimal? AverageCycleMin,
    decimal? VsBook,
    decimal? BenchmarkCycleMin,
    RoutePhasesData Phases);

public sealed record RoutesListData(IReadOnlyList<RouteData> Routes);

public static class RouteEndpoints
{
    public static void MapRouteEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/routes").CacheOutput("DataEndpoints");

        group.MapGet("", async Task<IResult> (
            DateOnly? from,
            DateOnly? to,
            string? shift,
            HaulCycleQueries queries,
            CancellationToken ct) =>
        {
            var routeRoster = await queries.GetRoutesAsync(ct);
            var meta = await queries.GetMetaAsync(ct);

            // Benchmarks come from ALL cycles ever recorded, never the requested window (see
            // CONTEXT.md's Benchmark definition) - fetch and compute them regardless of whether
            // there's any data in the window.
            var benchmarkCycles = await queries.GetBenchmarkCyclesAsync(ct);
            var benchmarks = RouteBenchmarkCalculator.Calculate(benchmarkCycles);

            if (meta.AsOf is null)
            {
                var emptyRoutes = routeRoster.Select(r => BuildRouteRow(r, [], benchmarks)).ToList();
                return Results.Ok(new Envelope<RoutesListData>(null, from, to, new RoutesListData(emptyRoutes)));
            }

            if (!RequestWindow.TryResolve(from, to, shift, meta.AsOf.Value, out var fromDt, out var toDt, out var fromDate, out var toDate, out var problem))
                return problem!;

            var cycles = await queries.GetCyclesAsync(fromDt, toDt, shift, ct);
            var cyclesByRoute = cycles.ToLookup(c => c.RouteName);

            var routes = routeRoster
                .Select(r => BuildRouteRow(r, cyclesByRoute[r.RouteName].ToList(), benchmarks))
                .ToList();

            return Results.Ok(new Envelope<RoutesListData>(meta.AsOf, fromDate, toDate, new RoutesListData(routes)));
        })
        .WithName("GetRoutes")
        .WithSummary("Per-route actual vs book/benchmark cycle time, for a date window.");
    }

    private static RouteData BuildRouteRow(
        RouteInfo route,
        IReadOnlyCollection<CycleRow> cycles,
        IReadOnlyDictionary<string, RouteBenchmarkCalculator.RouteBenchmark> benchmarks)
    {
        var cycleTime = CycleTimeCalculator.Calculate(cycles);
        var tonnes = cycles.Sum(c => c.PayloadTonnes);

        decimal? vsBook = cycleTime.AverageCycleMin.HasValue && route.BookCycleMin != 0
            ? cycleTime.AverageCycleMin.Value / route.BookCycleMin - 1m
            : null;

        benchmarks.TryGetValue(route.RouteName, out var benchmark);
        var phases = benchmark?.Phases;

        var averages = PhaseAverages(cycles);

        return new RouteData(
            route.RouteName,
            route.LoaderName,
            route.DestinationName,
            route.Material,
            route.DistanceKm,
            route.GradePercent,
            route.BookCycleMin,
            cycles.Count,
            tonnes,
            cycleTime.AverageCycleMin,
            vsBook,
            benchmark?.TotalCycleMin,
            new RoutePhasesData(
                new RoutePhaseData(averages.Queue, phases?.QueueMin, route.BookQueueMin),
                new RoutePhaseData(averages.Load, phases?.LoadMin, route.BookLoadMin),
                new RoutePhaseData(averages.Haul, phases?.HaulMin, route.BookHaulMin),
                new RoutePhaseData(averages.Dump, phases?.DumpMin, route.BookDumpMin),
                new RoutePhaseData(averages.Return, phases?.ReturnMin, route.BookReturnMin)));
    }

    private static (decimal? Queue, decimal? Load, decimal? Haul, decimal? Dump, decimal? Return) PhaseAverages(
        IReadOnlyCollection<CycleRow> cycles)
    {
        if (cycles.Count == 0)
            return (null, null, null, null, null);

        return (
            cycles.Average(c => c.QueueMin),
            cycles.Average(c => c.LoadMin),
            cycles.Average(c => c.HaulMin),
            cycles.Average(c => c.DumpMin),
            cycles.Average(c => c.ReturnMin));
    }
}
