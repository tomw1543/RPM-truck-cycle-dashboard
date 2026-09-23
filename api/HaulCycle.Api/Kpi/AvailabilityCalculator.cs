namespace HaulCycle.Api.Kpi;

/// <summary>
/// Availability = (calendar time - all delay time) / calendar time.
/// Utilisation = working time / available time, working time = sum of cycle minutes
/// (queue counts as working - the truck is on shift and in the loop). Available time
/// counts idle time as available-but-not-working: a truck that isn't in a cycle or a
/// delay is idle, and idle time depresses utilisation without depressing availability.
/// Effective utilisation = working time / calendar time.
///
/// Calendar time is truck-calendar-time summed over the trucks in scope (CalendarScope
/// per-truck minutes x truckCount) - not wall-clock window length - so a truck down for
/// the whole window still counts toward the denominator, and a shift filter shrinks
/// calendar time along with the cycles instead of leaving it at the full window.
///
/// Idle minutes are a residual, never read from a table: calendar - working - delay,
/// clamped at zero. There is no Idles table; idle is whatever calendar time cycles and
/// delays don't account for.
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
        decimal DelayMinutes,
        decimal IdleMinutes,
        decimal? IdlePercent);

    public static Result Calculate(
        DateTime from,
        DateTime to,
        string? shift,
        int truckCount,
        IReadOnlyCollection<CycleRow> cycles,
        IReadOnlyCollection<DelayRow> delays)
    {
        var calendarMinutes = CalendarScope.MinutesInScope(from, to, shift) * truckCount;

        if (calendarMinutes <= 0)
            return new Result(null, null, null, 0m, 0m, 0m, 0m, 0m, null);

        var delayMinutes = delays.Sum(d => CalendarScope.OverlapMinutesInScope(d.StartTime, d.EndTime, from, to, shift));
        var workingMinutes = cycles.Sum(c => c.TotalCycleMin);
        var availableMinutes = calendarMinutes - delayMinutes;
        if (availableMinutes < 0) availableMinutes = 0m;

        var idleMinutes = calendarMinutes - workingMinutes - delayMinutes;
        if (idleMinutes < 0) idleMinutes = 0m;
        var idlePercent = calendarMinutes > 0 ? idleMinutes / calendarMinutes : (decimal?)null;

        var availability = (calendarMinutes - delayMinutes) / calendarMinutes;

        var utilisation = availableMinutes > 0
            ? workingMinutes / availableMinutes
            : (decimal?)null;

        var effectiveUtilisation = workingMinutes / calendarMinutes;

        return new Result(
            availability, utilisation, effectiveUtilisation,
            workingMinutes, availableMinutes, calendarMinutes, delayMinutes,
            idleMinutes, idlePercent);
    }
}
