using HaulCycle.Api.Data;
using HaulCycle.Api.Kpi;

namespace HaulCycle.Api.Endpoints;

public sealed record ReasonCountData(string Reason, int Count);

public sealed record TruckComplianceData(
    string TruckName,
    string? RouteName,
    string? LoaderName,
    string? UnavailableReason,
    decimal? PlannedTonnes,
    decimal ActualTonnes,
    decimal? PlannedCycles,
    int ActualCycles,
    decimal? PercentOfPlan,
    decimal? AveragePayloadPercent,
    bool BelowTypical,
    ShortfallBucketsData? Shortfall);

public sealed record ShiftComplianceData(
    DateOnly ShiftDate,
    string ShiftName,
    bool IsComplete,
    decimal? PlannedTonnes,
    decimal ActualTonnes,
    decimal? PlannedCycles,
    int ActualCycles,
    decimal? PercentOfPlan,
    bool BelowTypical,
    int UnavailableCount,
    IReadOnlyList<ReasonCountData> UnavailableReasons,
    IReadOnlyList<TruckComplianceData> Trucks,
    ShortfallBucketsData? Shortfall);

public sealed record ShortfallBucketsData(
    decimal PayloadShort,
    decimal UnplannedDowntime,
    decimal QueueLoaderDelay,
    decimal QueueOverTrucking,
    decimal HaulOverTarget,
    decimal OtherOverTarget,
    decimal Residual);

public sealed record TruckShortfallData(string TruckName, string? RouteName, string? LoaderName, decimal Gap, ShortfallBucketsData Buckets);

public sealed record ShiftShortfallData(
    DateOnly ShiftDate,
    string ShiftName,
    bool IsComplete,
    decimal Gap,
    ShortfallBucketsData Buckets,
    IReadOnlyList<TruckShortfallData> Trucks);

public sealed record WindowShortfallData(decimal Gap, ShortfallBucketsData Buckets, int ShiftCount);

public sealed record ShiftSummaryData(DateOnly ShiftDate, string ShiftName, decimal PercentOfPlan);

public sealed record ComplianceSummaryData(
    decimal? PlannedTonnes,
    decimal ActualTonnes,
    decimal? PlannedCycles,
    int ActualCycles,
    decimal? PercentOfPlan,
    decimal? Baseline,
    int ShiftCount,
    ShiftSummaryData? Best,
    ShiftSummaryData? Worst,
    WindowShortfallData? Shortfall);

public sealed record ScheduleComplianceData(ComplianceSummaryData Summary, IReadOnlyList<ShiftComplianceData> Shifts);

/// <summary>/api/schedule/compliance: one row per shift (planned vs actual tonnes and cycles,
/// unavailable trucks, per-truck breakdown), on the shift-grained basis - see
/// ScheduleComplianceCalculator for the arithmetic and CONTEXT.md for the Percent of plan,
/// Baseline and Below typical definitions.</summary>
public static class ScheduleEndpoints
{
    public static void MapScheduleEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/schedule").CacheOutput("DataEndpoints");

        group.MapGet("/compliance", async Task<IResult> (
            DateOnly? from,
            DateOnly? to,
            string? shift,
            HaulCycleQueries queries,
            CancellationToken ct) =>
        {
            var meta = await queries.GetMetaAsync(ct);
            if (meta.AsOf is null)
                return Results.Ok(new Envelope<ScheduleComplianceData>(null, from, to, EmptyData()));

            if (!RequestWindow.TryResolve(from, to, shift, meta.AsOf.Value, out var fromDt, out var toDt, out var fromDate, out var toDate, out var problem))
                return problem!;

            // Shift-grained, same basis as plan vs actual: GetCyclesForShiftWindowAsync +
            // GetSchedulesAsync, not the raw-timestamp cycle window.
            var cycles = await queries.GetCyclesForShiftWindowAsync(fromDate, toDate, shift, ct);
            var schedules = await queries.GetSchedulesAsync(fromDate, toDate, shift, ct);

            var result = ScheduleComplianceCalculator.Calculate(cycles, schedules, meta.AsOf.Value);

            var delays = await queries.GetDelaysAsync(fromDt, toDt, ct);
            var loaderDelays = await queries.GetLoaderDelaysAsync(fromDt, toDt, ct);
            var routeRoster = await queries.GetRoutesAsync(ct);
            var trucks = await queries.GetTrucksAsync(ct);
            var routesByName = routeRoster.ToDictionary(
                r => r.RouteName,
                r => new RouteTargetRow(r.RouteName, r.TargetCycleMin, r.TargetQueueMin, r.TargetLoadMin, r.TargetHaulMin, r.TargetDumpMin, r.TargetReturnMin));
            var capacityByTruck = trucks.ToDictionary(t => t.Name, t => t.CapacityTonnes);

            var shortfall = ShortfallAttributionCalculator.Calculate(
                cycles, schedules, delays, loaderDelays, routesByName, capacityByTruck, meta.AsOf.Value);

            var shortfallByShift = shortfall.Shifts.ToDictionary(s => (s.ShiftDate, s.ShiftName));

            var data = new ScheduleComplianceData(
                MapSummary(result.Summary, shortfall.Window),
                result.Shifts.Select(s => MapShift(s, shortfallByShift.GetValueOrDefault((s.ShiftDate, s.ShiftName)))).ToList());

            return Results.Ok(new Envelope<ScheduleComplianceData>(meta.AsOf, fromDate, toDate, data));
        })
        .WithName("GetScheduleCompliance")
        .WithSummary("Planned vs actual tonnes and cycles per shift, with unavailable trucks and a per-truck breakdown, for a date window.");
    }

    private static ComplianceSummaryData MapSummary(ScheduleComplianceCalculator.SummaryResult s, ShortfallAttributionCalculator.WindowResult shortfall) =>
        new(
            s.PlannedTonnes,
            s.ActualTonnes,
            s.PlannedCycles,
            s.ActualCycles,
            s.PercentOfPlan,
            s.Baseline,
            s.ShiftCount,
            s.Best is null ? null : new ShiftSummaryData(s.Best.ShiftDate, s.Best.ShiftName, s.Best.PercentOfPlan),
            s.Worst is null ? null : new ShiftSummaryData(s.Worst.ShiftDate, s.Worst.ShiftName, s.Worst.PercentOfPlan),
            new WindowShortfallData(shortfall.Gap, MapBuckets(shortfall.Buckets), shortfall.ShiftCount));

    private static ShiftComplianceData MapShift(ScheduleComplianceCalculator.ShiftResult s, ShortfallAttributionCalculator.ShiftResult? shortfall)
    {
        var shortfallByTruck = shortfall?.Trucks.ToDictionary(t => t.TruckName) ?? [];
        return new(
            s.ShiftDate,
            s.ShiftName,
            s.IsComplete,
            s.PlannedTonnes,
            s.ActualTonnes,
            s.PlannedCycles,
            s.ActualCycles,
            s.PercentOfPlan,
            s.BelowTypical,
            s.UnavailableCount,
            s.UnavailableReasons.Select(r => new ReasonCountData(r.Reason, r.Count)).ToList(),
            s.Trucks.Select(t => MapTruck(t, shortfallByTruck.GetValueOrDefault(t.TruckName))).ToList(),
            shortfall is null ? null : new ShortfallBucketsData(
                shortfall.Buckets.PayloadShort, shortfall.Buckets.UnplannedDowntime, shortfall.Buckets.QueueLoaderDelay,
                shortfall.Buckets.QueueOverTrucking, shortfall.Buckets.HaulOverTarget, shortfall.Buckets.OtherOverTarget, shortfall.Buckets.Residual));
    }

    private static TruckComplianceData MapTruck(ScheduleComplianceCalculator.TruckShiftResult t, ShortfallAttributionCalculator.TruckResult? shortfall) =>
        new(
            t.TruckName,
            t.RouteName,
            t.LoaderName,
            t.UnavailableReason,
            t.PlannedTonnes,
            t.ActualTonnes,
            t.PlannedCycles,
            t.ActualCycles,
            t.PercentOfPlan,
            t.AveragePayloadPercent,
            t.BelowTypical,
            shortfall is null ? null : MapBuckets(shortfall.Buckets));

    private static ShortfallBucketsData MapBuckets(ShortfallAttributionCalculator.Buckets b) =>
        new(b.PayloadShort, b.UnplannedDowntime, b.QueueLoaderDelay, b.QueueOverTrucking, b.HaulOverTarget, b.OtherOverTarget, b.Residual);

    private static ScheduleComplianceData EmptyData() =>
        new(new ComplianceSummaryData(null, 0m, null, 0, null, null, 0, null, null, null), []);
}
