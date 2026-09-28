using HaulCycle.Api.Data;
using HaulCycle.Api.Kpi;

namespace HaulCycle.Api.Endpoints;

public sealed record PhaseMinutesData(decimal Queue, decimal Load, decimal Haul, decimal Dump, decimal Return, decimal Unattributed);

public sealed record NamedAmountData(string Name, decimal Minutes, decimal? EquivalentTonnes);

public sealed record RoutePhaseMinutesData(string RouteName, decimal TotalMin, decimal? EquivalentTonnes, PhaseMinutesData Phases);

public sealed record RecoverableData(
    decimal TotalMin,
    decimal TotalEquivalentTonnes,
    PhaseMinutesData ByPhase,
    IReadOnlyList<NamedAmountData> ByRoute,
    IReadOnlyList<NamedAmountData> ByTruck,
    IReadOnlyList<NamedAmountData> ByLoader);

public sealed record OverTargetData(
    decimal TotalMin,
    decimal TotalEquivalentTonnes,
    PhaseMinutesData ByPhase,
    IReadOnlyList<RoutePhaseMinutesData> ByRoute);

public sealed record TruckUnderloadData(string TruckName, int Cycles, decimal UnderloadTonnes, decimal? AveragePayloadPercent);

public sealed record UnderloadData(decimal TotalTonnes, IReadOnlyList<TruckUnderloadData> ByTruck, decimal BaselinePayloadPercent);

public sealed record LossItemData(string Measure, string Subject, string? Phase, decimal NativeAmount, string NativeUnit, decimal EquivalentTonnes);

public sealed record BottlenecksData(
    RecoverableData Recoverable,
    OverTargetData OverTarget,
    UnderloadData Underload,
    IReadOnlyList<LossItemData> BiggestLosses);

/// <summary>/api/bottlenecks: where the fleet loses time and tonnes against the route P25
/// benchmark (recoverable minutes), against target rates (minutes over target), and to
/// part-filled trucks (underload tonnes). See CONTEXT.md for each measure's definition, and the
/// Kpi/*Calculator classes for the arithmetic - this endpoint only fetches rows and maps
/// calculator results onto the response DTOs above.</summary>
public static class BottleneckEndpoints
{
    public static void MapBottleneckEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/bottlenecks").CacheOutput("DataEndpoints");

        group.MapGet("", async Task<IResult> (
            DateOnly? from,
            DateOnly? to,
            string? shift,
            HaulCycleQueries queries,
            CancellationToken ct) =>
        {
            var meta = await queries.GetMetaAsync(ct);
            if (meta.AsOf is null)
                return Results.Ok(new Envelope<BottlenecksData>(null, from, to, EmptyData()));

            if (!RequestWindow.TryResolve(from, to, shift, meta.AsOf.Value, out var fromDt, out var toDt, out var fromDate, out var toDate, out var problem))
                return problem!;

            var windowCycles = await queries.GetCyclesAsync(fromDt, toDt, shift, ct);
            var benchmarkCycles = await queries.GetBenchmarkCyclesAsync(ct);
            var routeRoster = await queries.GetRoutesAsync(ct);

            var benchmarks = RouteBenchmarkCalculator.Calculate(benchmarkCycles);
            var ratesByRoute = EquivalentTonnesCalculator.RatesByRoute(windowCycles);
            var routesByName = routeRoster.ToDictionary(
                r => r.RouteName,
                r => new RouteTargetRow(r.RouteName, r.TargetCycleMin, r.TargetQueueMin, r.TargetLoadMin, r.TargetHaulMin, r.TargetDumpMin, r.TargetReturnMin));

            var recoverable = RecoverableMinutesCalculator.Calculate(windowCycles, benchmarks, ratesByRoute);
            var overTarget = MinutesOverTargetCalculator.Calculate(windowCycles, routesByName, ratesByRoute);
            var underload = UnderloadCalculator.Calculate(windowCycles, benchmarkCycles);
            var biggestLosses = BiggestLossesCalculator.Calculate(overTarget, underload, ratesByRoute);

            var data = new BottlenecksData(
                MapRecoverable(recoverable),
                MapOverTarget(overTarget),
                MapUnderload(underload),
                biggestLosses.Select(i => new LossItemData(i.Measure, i.Subject, i.Phase, i.NativeAmount, i.NativeUnit, i.EquivalentTonnes)).ToList());

            return Results.Ok(new Envelope<BottlenecksData>(meta.AsOf, fromDate, toDate, data));
        })
        .WithName("GetBottlenecks")
        .WithSummary("Recoverable minutes, minutes over target, underload tonnes and biggest losses, for a date window.");
    }

    private static RecoverableData MapRecoverable(RecoverableMinutesCalculator.Result r) =>
        new(
            r.TotalMin,
            r.TotalEquivalentTonnes,
            MapPhase(r.ByPhase),
            r.ByRoute.Select(MapAmount).ToList(),
            r.ByTruck.Select(MapAmount).ToList(),
            r.ByLoader.Select(MapAmount).ToList());

    private static OverTargetData MapOverTarget(MinutesOverTargetCalculator.Result r) =>
        new(
            r.TotalMin,
            r.TotalEquivalentTonnes,
            MapPhase(r.ByPhase),
            r.ByRoute.Select(x => new RoutePhaseMinutesData(x.RouteName, x.TotalMin, x.EquivalentTonnes, MapPhase(x.Phases))).ToList());

    private static UnderloadData MapUnderload(UnderloadCalculator.Result r) =>
        new(r.TotalTonnes, r.ByTruck.Select(t => new TruckUnderloadData(t.TruckName, t.Cycles, t.UnderloadTonnes, t.AveragePayloadPercent)).ToList(), r.BaselinePayloadPercent);

    private static NamedAmountData MapAmount(RecoverableMinutesCalculator.NamedAmount a) => new(a.Name, a.Minutes, a.EquivalentTonnes);

    private static PhaseMinutesData MapPhase(PhaseAttributionCalculator.PhaseAmounts p) =>
        new(p.Queue, p.Load, p.Haul, p.Dump, p.Return, p.Unattributed);

    private static BottlenecksData EmptyData() =>
        new(
            new RecoverableData(0m, 0m, ZeroPhase, [], [], []),
            new OverTargetData(0m, 0m, ZeroPhase, []),
            new UnderloadData(0m, [], 0m),
            []);

    private static readonly PhaseMinutesData ZeroPhase = new(0m, 0m, 0m, 0m, 0m, 0m);
}
