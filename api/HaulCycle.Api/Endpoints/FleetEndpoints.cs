using HaulCycle.Api.Data;
using HaulCycle.Api.Kpi;

namespace HaulCycle.Api.Endpoints;

public sealed record FleetSummaryData(
    int Cycles,
    decimal Tonnes,
    decimal? TonnesPerOperatingHour,
    decimal? TonnesPerCalendarHour,
    decimal? AverageCycleMin,
    CycleTimeCalculator.PhaseSplit? PhaseSplit,
    decimal? Availability,
    decimal? Utilisation,
    decimal? EffectiveUtilisation,
    decimal IdleMinutes,
    decimal? IdlePercent,
    decimal? MatchFactor,
    PlanVsActualCalculator.Result PlanVsActual);

public static class FleetEndpoints
{
    public static void MapFleetEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/fleet").CacheOutput("DataEndpoints");

        group.MapGet("/summary", async Task<IResult> (
            DateOnly? from,
            DateOnly? to,
            string? shift,
            HaulCycleQueries queries,
            CancellationToken ct) =>
        {
            var meta = await queries.GetMetaAsync(ct);
            if (meta.AsOf is null)
            {
                // No data at all - still a valid (empty) response, not an error.
                var empty = new FleetSummaryData(0, 0m, null, null, null, null, null, null, null, 0m, null, null,
                    new PlanVsActualCalculator.Result(0m, null, null, [], true, 0));
                return Results.Ok(new Envelope<FleetSummaryData>(null, from, to, empty));
            }

            if (!RequestWindow.TryResolve(from, to, shift, meta.AsOf.Value, out var fromDt, out var toDt, out var fromDate, out var toDate, out var problem))
                return problem!;

            var cycles = await queries.GetCyclesAsync(fromDt, toDt, shift, ct);
            var delays = await queries.GetDelaysAsync(fromDt, toDt, ct);
            var schedules = await queries.GetSchedulesAsync(fromDate, toDate, shift, ct);
            // Plan vs actual is shift-grained: its cycles come from the same ShiftDate window as
            // schedules, not the raw-timestamp window used by the cycle-grained KPIs above.
            var planCycles = await queries.GetCyclesForShiftWindowAsync(fromDate, toDate, shift, ct);

            var tonnesPerHour = TonnesPerHourCalculator.Calculate(cycles, fromDt, toDt, shift, meta.Counts.Trucks);
            var cycleTime = CycleTimeCalculator.Calculate(cycles);
            var availability = AvailabilityCalculator.Calculate(fromDt, toDt, shift, meta.Counts.Trucks, cycles, delays);
            var matchFactor = MatchFactorCalculator.Calculate(cycles, meta.Counts.Trucks, meta.Counts.Loaders);
            var planVsActual = PlanVsActualCalculator.Calculate(planCycles, schedules, meta.AsOf.Value);

            var data = new FleetSummaryData(
                cycles.Count,
                tonnesPerHour.TotalTonnes,
                tonnesPerHour.TonnesPerOperatingHour,
                tonnesPerHour.TonnesPerCalendarHour,
                cycleTime.AverageCycleMin,
                cycleTime.PhaseSplit,
                availability.Availability,
                availability.Utilisation,
                availability.EffectiveUtilisation,
                availability.IdleMinutes,
                availability.IdlePercent,
                matchFactor,
                planVsActual);

            return Results.Ok(new Envelope<FleetSummaryData>(meta.AsOf, fromDate, toDate, data));
        })
        .WithName("GetFleetSummary")
        .WithSummary("Fleet-wide production and utilisation KPIs for a date window.");
    }
}
