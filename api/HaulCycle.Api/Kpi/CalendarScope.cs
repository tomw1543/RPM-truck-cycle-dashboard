namespace HaulCycle.Api.Kpi;

/// <summary>
/// Single shared source of truth for "calendar minutes in scope" - used by every KPI that
/// has a calendar-time denominator (availability, utilisation, effective utilisation, tonnes
/// per calendar hour) so the shift filter can't drift out of sync between them.
///
/// With no shift filter, scope is the whole [from, to) window. With shift = Day or Night,
/// scope is only the hours belonging to that shift (Day 06:00-18:00, Night 18:00-06:00, mine
/// time) inside [from, to) - so a shift filter shrinks calendar time along with the cycles,
/// instead of leaving calendar time at the full window while cycles halve.
/// </summary>
public static class CalendarScope
{
    /// <summary>Calendar minutes in [from, to), restricted to `shift`'s hours (null = whole window).
    /// This is PER TRUCK; callers multiply by the number of trucks in scope for fleet calendar time.</summary>
    public static decimal MinutesInScope(DateTime from, DateTime to, string? shift) =>
        OverlapMinutesInScope(from, to, from, to, shift);

    /// <summary>Minutes of [start, end) that fall inside both [from, to) and, if `shift` is given,
    /// that shift's hours. Used to clip delay (and similar interval) minutes the same way calendar
    /// time itself is clipped, so a shift filter can't leave delay minutes counted from outside it.</summary>
    public static decimal OverlapMinutesInScope(DateTime start, DateTime end, DateTime from, DateTime to, string? shift)
    {
        var s = start > from ? start : from;
        var e = end < to ? end : to;
        if (e <= s) return 0m;

        if (shift is null)
            return (decimal)(e - s).TotalMinutes;

        decimal total = 0m;
        var cursor = ShiftBoundaryAtOrBefore(s);
        while (cursor < e)
        {
            var next = cursor.AddHours(12);
            var segStart = cursor > s ? cursor : s;
            var segEnd = next < e ? next : e;
            if (segEnd > segStart)
            {
                var segShift = cursor.Hour == 6 ? "Day" : "Night";
                if (segShift == shift)
                    total += (decimal)(segEnd - segStart).TotalMinutes;
            }
            cursor = next;
        }
        return total;
    }

    private static DateTime ShiftBoundaryAtOrBefore(DateTime t)
    {
        var six = t.Date.AddHours(6);
        var eighteen = t.Date.AddHours(18);
        if (t >= eighteen) return eighteen;
        if (t >= six) return six;
        return t.Date.AddDays(-1).AddHours(18);
    }
}
