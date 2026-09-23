namespace HaulCycle.Api.Kpi;

/// <summary>Plan vs actual = actual tonnes in the window / planned tonnes from Schedules,
/// overall and per destination. Unavailable-truck schedule rows contribute no plan.</summary>
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
        IReadOnlyList<DestinationResult> ByDestination);

    public static Result Calculate(IReadOnlyCollection<CycleRow> cycles, IReadOnlyCollection<ScheduleRow> schedules)
    {
        var actualTotal = cycles.Sum(c => c.PayloadTonnes);

        var hasAnyPlan = schedules.Any(s => s.PlannedTonnes.HasValue);
        decimal? plannedTotal = hasAnyPlan ? schedules.Sum(s => s.PlannedTonnes ?? 0m) : null;
        var percentOfPlan = plannedTotal is > 0 ? actualTotal / plannedTotal : null;

        var actualByDestination = cycles
            .GroupBy(c => c.DestinationName)
            .ToDictionary(g => g.Key, g => g.Sum(c => c.PayloadTonnes));

        var plannedByDestination = schedules
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

        return new Result(actualTotal, plannedTotal, percentOfPlan, byDestination);
    }
}
