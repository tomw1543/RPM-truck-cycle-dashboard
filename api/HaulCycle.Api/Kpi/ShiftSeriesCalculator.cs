namespace HaulCycle.Api.Kpi;

/// <summary>One truck's shift-by-shift series: assigned route (or unavailability reason),
/// planned vs actual tonnes, cycle count and average payload percent, for the truck detail
/// page's plan-vs-actual chart.
///
/// Callers must pass cycles and schedules from the shift-grained basis (GetCyclesForShiftWindowAsync
/// + GetSchedulesAsync, both already filtered to ShiftDate/ShiftName in [fromDate, toDate]) - the
/// same basis PlanVsActualCalculator uses - not the raw-timestamp cycle window, so a shift's cycles
/// are attributed to their whole shift rather than clipped by the request's literal from/to.
///
/// A shift with a schedule row but no cycles (truck unavailable, or available but idle all shift)
/// still appears, with Cycles = 0 and ActualTonnes = 0. A shift with cycles but no schedule row
/// (shouldn't normally happen - every truck gets a schedule row every shift - but handled defensively)
/// appears with RouteName/UnavailableReason/PlannedTonnes all null.</summary>
public static class ShiftSeriesCalculator
{
    public sealed record Result(
        DateOnly ShiftDate,
        string ShiftName,
        string? RouteName,
        string? UnavailableReason,
        decimal? PlannedTonnes,
        decimal ActualTonnes,
        int Cycles,
        decimal? AveragePayloadPercent);

    public static IReadOnlyList<Result> Calculate(IReadOnlyCollection<CycleRow> cycles, IReadOnlyCollection<ScheduleRow> schedules)
    {
        var cyclesByShift = cycles
            .GroupBy(c => (c.ShiftDate, c.ShiftName))
            .ToDictionary(g => g.Key, g => g.ToList());

        var schedulesByShift = schedules
            .ToDictionary(s => (s.ShiftDate, s.ShiftName));

        var keys = schedulesByShift.Keys
            .Concat(cyclesByShift.Keys)
            .Distinct()
            .OrderBy(k => k.ShiftDate)
            .ThenBy(k => k.ShiftName == "Day" ? 0 : 1);

        var results = new List<Result>();
        foreach (var key in keys)
        {
            schedulesByShift.TryGetValue(key, out var schedule);
            var shiftCycles = cyclesByShift.TryGetValue(key, out var list) ? list : [];

            var actualTonnes = shiftCycles.Sum(c => c.PayloadTonnes);
            decimal? averagePayloadPercent = shiftCycles.Count > 0 ? shiftCycles.Average(c => c.PayloadPercentOfCapacity) : null;

            results.Add(new Result(
                key.ShiftDate,
                key.ShiftName,
                schedule?.RouteName,
                schedule?.UnavailableReason,
                schedule?.PlannedTonnes,
                actualTonnes,
                shiftCycles.Count,
                averagePayloadPercent));
        }

        return results;
    }
}
