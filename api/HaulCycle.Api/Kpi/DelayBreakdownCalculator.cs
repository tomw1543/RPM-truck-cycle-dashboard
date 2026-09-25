namespace HaulCycle.Api.Kpi;

/// <summary>Delay minutes and counts grouped by reason (and planned/unplanned), for one truck's
/// detail page. Minutes are clipped to the window - and to the shift filter, if one is given -
/// the same way AvailabilityCalculator clips delay minutes, via CalendarScope.OverlapMinutesInScope,
/// so a delay straddling the window edge only contributes the portion inside it. A delay with no
/// overlap in scope (can happen once a shift filter is applied) contributes nothing and its reason
/// is dropped entirely rather than showing a zero-minute row. Sorted by minutes descending.</summary>
public static class DelayBreakdownCalculator
{
    public sealed record Result(string Reason, bool IsPlanned, int Count, decimal Minutes);

    public static IReadOnlyList<Result> Calculate(
        IReadOnlyCollection<DelayRow> delays, DateTime from, DateTime to, string? shift)
    {
        return delays
            .Select(d => (d.Reason, d.IsPlanned, Minutes: CalendarScope.OverlapMinutesInScope(d.StartTime, d.EndTime, from, to, shift)))
            .Where(d => d.Minutes > 0)
            .GroupBy(d => (d.Reason, d.IsPlanned))
            .Select(g => new Result(g.Key.Reason, g.Key.IsPlanned, g.Count(), g.Sum(x => x.Minutes)))
            .OrderByDescending(r => r.Minutes)
            .ToList();
    }
}
