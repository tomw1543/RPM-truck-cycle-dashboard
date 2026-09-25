using HaulCycle.Api.Data;
using HaulCycle.Api.Kpi;

namespace HaulCycle.Api.Endpoints;

/// <summary>One truck's (or the fleet average's) KPIs for a window.
///
/// Cycles and Tonnes are per-truck values: for a truck row they're that truck's own totals; for
/// the fleet row (Name = "Fleet average") they're the fleet total divided by truck count, i.e.
/// the average truck's cycles/tonnes - so the fleet row reads like "an average truck" alongside
/// the real ones. Every other field is a rate or ratio and is computed fleet-wide exactly as
/// /api/fleet/summary computes it (calendar time = truck count x window length via CalendarScope),
/// not by averaging the per-truck rates - a truck absent from the window still counts toward the
/// fleet's calendar time. Ratios stay 0-1 fractions; AveragePayloadPercent is 0-100, matching
/// CycleRow.PayloadPercentOfCapacity.</summary>
public sealed record TruckKpis(
    string Name,
    decimal CapacityTonnes,
    decimal Cycles,
    decimal Tonnes,
    decimal? TonnesPerOperatingHour,
    decimal? TonnesPerCalendarHour,
    decimal? AverageCycleMin,
    decimal? AveragePayloadPercent,
    decimal? CyclesPerOperatingHour,
    decimal? Availability,
    decimal? Utilisation,
    decimal? EffectiveUtilisation,
    decimal? IdlePercent);

public sealed record TrucksListData(TruckKpis Fleet, IReadOnlyList<TruckKpis> Trucks);

public sealed record DelayReasonData(string Reason, bool IsPlanned, int Count, decimal Minutes);

public sealed record ShiftSeriesData(
    DateOnly ShiftDate,
    string ShiftName,
    string? RouteName,
    string? UnavailableReason,
    decimal? PlannedTonnes,
    decimal ActualTonnes,
    int Cycles,
    decimal? AveragePayloadPercent,
    bool IsComplete);

public sealed record TruckDetailData(
    TruckKpis Truck,
    TruckKpis Fleet,
    CycleTimeCalculator.PhaseSplit? PhaseSplit,
    IReadOnlyList<DelayReasonData> DelaysByReason,
    IReadOnlyList<ShiftSeriesData> Shifts);

public static class TruckEndpoints
{
    public static void MapTruckEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/trucks").CacheOutput("DataEndpoints");

        group.MapGet("", async Task<IResult> (
            DateOnly? from,
            DateOnly? to,
            string? shift,
            HaulCycleQueries queries,
            CancellationToken ct) =>
        {
            var meta = await queries.GetMetaAsync(ct);
            var roster = await queries.GetTrucksAsync(ct);
            var averageCapacity = roster.Count > 0 ? roster.Average(t => t.CapacityTonnes) : 0m;

            if (meta.AsOf is null)
            {
                var emptyFleet = EmptyKpis("Fleet average", averageCapacity);
                var emptyTrucks = roster.Select(t => EmptyKpis(t.Name, t.CapacityTonnes)).ToList();
                return Results.Ok(new Envelope<TrucksListData>(null, from, to, new TrucksListData(emptyFleet, emptyTrucks)));
            }

            if (!RequestWindow.TryResolve(from, to, shift, meta.AsOf.Value, out var fromDt, out var toDt, out var fromDate, out var toDate, out var problem))
                return problem!;

            var cycles = await queries.GetCyclesAsync(fromDt, toDt, shift, ct);
            var delays = await queries.GetDelaysAsync(fromDt, toDt, ct);

            var cyclesByTruck = cycles.ToLookup(c => c.TruckName);
            var delaysByTruck = delays.ToLookup(d => d.TruckName);

            var truckRows = roster
                .Select(t => ComputeTruckRow(t.Name, t.CapacityTonnes, cyclesByTruck[t.Name].ToList(), delaysByTruck[t.Name].ToList(), fromDt, toDt, shift))
                .ToList();

            var fleetRow = ComputeFleetRow(cycles, delays, fromDt, toDt, shift, meta.Counts.Trucks, averageCapacity);

            return Results.Ok(new Envelope<TrucksListData>(meta.AsOf, fromDate, toDate, new TrucksListData(fleetRow, truckRows)));
        })
        .WithName("GetTrucks")
        .WithSummary("Per-truck production and utilisation KPIs, plus a fleet-average row, for a date window.");

        group.MapGet("/{name}", async Task<IResult> (
            string name,
            DateOnly? from,
            DateOnly? to,
            string? shift,
            HaulCycleQueries queries,
            CancellationToken ct) =>
        {
            var roster = await queries.GetTrucksAsync(ct);
            var truck = roster.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
            if (truck is null)
            {
                return TypedResults.Problem(
                    title: "Truck not found",
                    detail: $"No truck named {name}.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            var meta = await queries.GetMetaAsync(ct);
            var averageCapacity = roster.Count > 0 ? roster.Average(t => t.CapacityTonnes) : 0m;

            if (meta.AsOf is null)
            {
                var emptyData = new TruckDetailData(
                    EmptyKpis(truck.Name, truck.CapacityTonnes),
                    EmptyKpis("Fleet average", averageCapacity),
                    null, [], []);
                return Results.Ok(new Envelope<TruckDetailData>(null, from, to, emptyData));
            }

            if (!RequestWindow.TryResolve(from, to, shift, meta.AsOf.Value, out var fromDt, out var toDt, out var fromDate, out var toDate, out var problem))
                return problem!;

            var allCycles = await queries.GetCyclesAsync(fromDt, toDt, shift, ct);
            var allDelays = await queries.GetDelaysAsync(fromDt, toDt, ct);
            var truckCycles = allCycles.Where(c => c.TruckName == truck.Name).ToList();
            var truckDelays = allDelays.Where(d => d.TruckName == truck.Name).ToList();

            var truckKpis = ComputeTruckRow(truck.Name, truck.CapacityTonnes, truckCycles, truckDelays, fromDt, toDt, shift);
            var fleetKpis = ComputeFleetRow(allCycles, allDelays, fromDt, toDt, shift, meta.Counts.Trucks, averageCapacity);

            var phaseSplit = CycleTimeCalculator.Calculate(truckCycles).PhaseSplit;

            var delaysByReason = DelayBreakdownCalculator.Calculate(truckDelays, fromDt, toDt, shift)
                .Select(r => new DelayReasonData(r.Reason, r.IsPlanned, r.Count, r.Minutes))
                .ToList();

            // Shift-grained: same basis as plan vs actual (GetCyclesForShiftWindowAsync + GetSchedulesAsync),
            // not the raw-timestamp cycle window above.
            var shiftCycles = (await queries.GetCyclesForShiftWindowAsync(fromDate, toDate, shift, ct))
                .Where(c => c.TruckName == truck.Name)
                .ToList();
            var shiftSchedules = (await queries.GetSchedulesAsync(fromDate, toDate, shift, ct))
                .Where(s => s.TruckName == truck.Name)
                .ToList();

            var shifts = ShiftSeriesCalculator.Calculate(shiftCycles, shiftSchedules, meta.AsOf.Value)
                .Select(r => new ShiftSeriesData(r.ShiftDate, r.ShiftName, r.RouteName, r.UnavailableReason, r.PlannedTonnes, r.ActualTonnes, r.Cycles, r.AveragePayloadPercent, r.IsComplete))
                .ToList();

            var data = new TruckDetailData(truckKpis, fleetKpis, phaseSplit, delaysByReason, shifts);
            return Results.Ok(new Envelope<TruckDetailData>(meta.AsOf, fromDate, toDate, data));
        })
        .WithName("GetTruckDetail")
        .WithSummary("One truck's KPIs, phase split, delay breakdown and shift-by-shift plan vs actual, for a date window.");
    }

    private static TruckKpis EmptyKpis(string name, decimal capacityTonnes) =>
        new(name, capacityTonnes, 0m, 0m, null, null, null, null, null, null, null, null, null);

    private static TruckKpis ComputeTruckRow(
        string name,
        decimal capacityTonnes,
        IReadOnlyCollection<CycleRow> cycles,
        IReadOnlyCollection<DelayRow> delays,
        DateTime from,
        DateTime to,
        string? shift)
    {
        var tonnesPerHour = TonnesPerHourCalculator.Calculate(cycles, from, to, shift, truckCount: 1);
        var cycleTime = CycleTimeCalculator.Calculate(cycles);
        var availability = AvailabilityCalculator.Calculate(from, to, shift, truckCount: 1, cycles, delays);
        var payloadRate = PayloadRateCalculator.Calculate(cycles);

        return new TruckKpis(
            name,
            capacityTonnes,
            cycles.Count,
            tonnesPerHour.TotalTonnes,
            tonnesPerHour.TonnesPerOperatingHour,
            tonnesPerHour.TonnesPerCalendarHour,
            cycleTime.AverageCycleMin,
            payloadRate.AveragePayloadPercent,
            payloadRate.CyclesPerOperatingHour,
            availability.Availability,
            availability.Utilisation,
            availability.EffectiveUtilisation,
            availability.IdlePercent);
    }

    /// <summary>Fleet-wide rates (as /api/fleet/summary computes them), but Cycles/Tonnes divided
    /// down to a per-truck average so the row reads alongside real truck rows.</summary>
    private static TruckKpis ComputeFleetRow(
        IReadOnlyCollection<CycleRow> cycles,
        IReadOnlyCollection<DelayRow> delays,
        DateTime from,
        DateTime to,
        string? shift,
        int truckCount,
        decimal averageCapacityTonnes)
    {
        var tonnesPerHour = TonnesPerHourCalculator.Calculate(cycles, from, to, shift, truckCount);
        var cycleTime = CycleTimeCalculator.Calculate(cycles);
        var availability = AvailabilityCalculator.Calculate(from, to, shift, truckCount, cycles, delays);
        var payloadRate = PayloadRateCalculator.Calculate(cycles);

        var perTruckCycles = truckCount > 0 ? cycles.Count / (decimal)truckCount : 0m;
        var perTruckTonnes = truckCount > 0 ? tonnesPerHour.TotalTonnes / truckCount : 0m;

        return new TruckKpis(
            "Fleet average",
            averageCapacityTonnes,
            perTruckCycles,
            perTruckTonnes,
            tonnesPerHour.TonnesPerOperatingHour,
            tonnesPerHour.TonnesPerCalendarHour,
            cycleTime.AverageCycleMin,
            payloadRate.AveragePayloadPercent,
            payloadRate.CyclesPerOperatingHour,
            availability.Availability,
            availability.Utilisation,
            availability.EffectiveUtilisation,
            availability.IdlePercent);
    }
}
