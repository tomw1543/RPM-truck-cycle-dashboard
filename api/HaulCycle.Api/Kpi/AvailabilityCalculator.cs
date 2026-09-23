namespace HaulCycle.Api.Kpi;

/// <summary>
/// Availability = (calendar time - all delay time) / calendar time.
/// Utilisation = working time / available time, working time = sum of cycle minutes
/// (queue counts as working - the truck is on shift and in the loop).
/// Effective utilisation = working time / calendar time.
///
/// Calendar time is truckCount x the window length, so a truck down for the whole
/// window still counts toward the denominator even though it produced no cycles.
/// </summary>
public static class AvailabilityCalculator
{
    public sealed record Result(
        decimal? Availability,
        decimal? Utilisation,
        decimal? EffectiveUtilisation,
        decimal WorkingMinutes,
        decimal AvailableMinutes,
        decimal CalendarMinutes,
        decimal DelayMinutes);

    public static Result Calculate(
        DateTime from,
        DateTime to,
        int truckCount,
        IReadOnlyCollection<CycleRow> cycles,
        IReadOnlyCollection<DelayRow> delays)
    {
        var windowMinutes = to > from ? (decimal)(to - from).TotalMinutes : 0m;
        var calendarMinutes = windowMinutes * truckCount;

        if (calendarMinutes <= 0)
            return new Result(null, null, null, 0m, 0m, 0m, 0m);

        var delayMinutes = delays.Sum(d => OverlapMinutes(d.StartTime, d.EndTime, from, to));
        var workingMinutes = cycles.Sum(c => c.TotalCycleMin);
        var availableMinutes = calendarMinutes - delayMinutes;
        if (availableMinutes < 0) availableMinutes = 0m;

        var availability = calendarMinutes > 0
            ? (calendarMinutes - delayMinutes) / calendarMinutes
            : (decimal?)null;

        var utilisation = availableMinutes > 0
            ? workingMinutes / availableMinutes
            : (decimal?)null;

        var effectiveUtilisation = calendarMinutes > 0
            ? workingMinutes / calendarMinutes
            : (decimal?)null;

        return new Result(availability, utilisation, effectiveUtilisation, workingMinutes, availableMinutes, calendarMinutes, delayMinutes);
    }

    private static decimal OverlapMinutes(DateTime delayStart, DateTime delayEnd, DateTime from, DateTime to)
    {
        var start = delayStart > from ? delayStart : from;
        var end = delayEnd < to ? delayEnd : to;
        return end > start ? (decimal)(end - start).TotalMinutes : 0m;
    }
}
