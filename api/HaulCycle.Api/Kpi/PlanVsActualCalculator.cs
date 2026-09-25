namespace HaulCycle.Api.Kpi;

/// <summary>Plan vs actual = actual tonnes in the window / planned tonnes from Schedules,
/// overall and per destination. Unavailable-truck schedule rows contribute no plan.
///
/// Only complete shifts count: a shift that hasn't finished as of `asOf` has its full
/// planned tonnes but only partial actual tonnes so far, which drags the ratio down for a
/// reason that has nothing to do with performance. Cycles and schedules belonging to any
/// shift whose end time is after `asOf` are excluded from both totals, both overall and
/// per destination. `CompleteShiftsOnly` is always true in the result (there's no toggle -
/// this is the only supported behaviour) and `ExcludedShiftCount` reports how many distinct
/// truck-shifts (each schedule/cycle row's ShiftDate+ShiftName) were left out.</summary>
public static class PlanVsActualCalculator
{
    public sealed record DestinationResult(
        string Destination,
        decimal ActualTonnes,
        decimal? PlannedTonnes,
        decimal? PercentOfPlan);

    public sealed record Result(
        decimal ActualTonnes,
        decimal? PlannedTonnes,
        decimal? PercentOfPlan,
        IReadOnlyList<DestinationResult> ByDestination,
        bool CompleteShiftsOnly,
        int ExcludedShiftCount);

    public static Result Calculate(IReadOnlyCollection<CycleRow> cycles, IReadOnlyCollection<ScheduleRow> schedules, DateTime asOf)
    {
        var incompleteShifts = cycles.Select(c => (c.ShiftDate, c.ShiftName))
            .Concat(schedules.Select(s => (s.ShiftDate, s.ShiftName)))
            .Distinct()
            .Where(shift => ShiftWindow.End(shift.ShiftDate, shift.ShiftName) > asOf)
            .ToHashSet();

        var completeCycles = cycles.Where(c => !incompleteShifts.Contains((c.ShiftDate, c.ShiftName))).ToList();
        var completeSchedules = schedules.Where(s => !incompleteShifts.Contains((s.ShiftDate, s.ShiftName))).ToList();

        var actualTotal = completeCycles.Sum(c => c.PayloadTonnes);

        var hasAnyPlan = completeSchedules.Any(s => s.PlannedTonnes.HasValue);
        decimal? plannedTotal = hasAnyPlan ? completeSchedules.Sum(s => s.PlannedTonnes ?? 0m) : null;
        var percentOfPlan = plannedTotal is > 0 ? actualTotal / plannedTotal : null;

        var actualByDestination = completeCycles
            .GroupBy(c => c.DestinationName)
            .ToDictionary(g => g.Key, g => g.Sum(c => c.PayloadTonnes));

        var plannedByDestination = completeSchedules
            .Where(s => s.DestinationName is not null && s.PlannedTonnes.HasValue)
            .GroupBy(s => s.DestinationName!)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.PlannedTonnes!.Value));

        var destinations = actualByDestination.Keys.Union(plannedByDestination.Keys).OrderBy(d => d, StringComparer.Ordinal);

        var byDestination = new List<DestinationResult>();
        foreach (var destination in destinations)
        {
            var actual = actualByDestination.GetValueOrDefault(destination, 0m);
            var planned = plannedByDestination.TryGetValue(destination, out var p) ? p : (decimal?)null;
            var percent = planned is > 0 ? actual / planned : null;
            byDestination.Add(new DestinationResult(destination, actual, planned, percent));
        }

        return new Result(actualTotal, plannedTotal, percentOfPlan, byDestination, true, incompleteShifts.Count);
    }
}
