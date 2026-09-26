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
    bool BelowTypical);

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
    IReadOnlyList<TruckComplianceData> Trucks);

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
    ShiftSummaryData? Worst);

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

            var data = new ScheduleComplianceData(MapSummary(result.Summary), result.Shifts.Select(MapShift).ToList());

            return Results.Ok(new Envelope<ScheduleComplianceData>(meta.AsOf, fromDate, toDate, data));
        })
        .WithName("GetScheduleCompliance")
        .WithSummary("Planned vs actual tonnes and cycles per shift, with unavailable trucks and a per-truck breakdown, for a date window.");
    }

    private static ComplianceSummaryData MapSummary(ScheduleComplianceCalculator.SummaryResult s) =>
        new(
            s.PlannedTonnes,
            s.ActualTonnes,
            s.PlannedCycles,
            s.ActualCycles,
            s.PercentOfPlan,
            s.Baseline,
            s.ShiftCount,
            s.Best is null ? null : new ShiftSummaryData(s.Best.ShiftDate, s.Best.ShiftName, s.Best.PercentOfPlan),
            s.Worst is null ? null : new ShiftSummaryData(s.Worst.ShiftDate, s.Worst.ShiftName, s.Worst.PercentOfPlan));

    private static ShiftComplianceData MapShift(ScheduleComplianceCalculator.ShiftResult s) =>
        new(
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
            s.Trucks.Select(MapTruck).ToList());

    private static TruckComplianceData MapTruck(ScheduleComplianceCalculator.TruckShiftResult t) =>
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
            t.BelowTypical);

    private static ScheduleComplianceData EmptyData() =>
        new(new ComplianceSummaryData(null, 0m, null, 0, null, null, 0, null, null), []);
}
