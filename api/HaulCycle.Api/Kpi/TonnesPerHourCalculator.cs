namespace HaulCycle.Api.Kpi;

/// <summary>
/// Tonnes per operating hour rates the truck (tonnes / hours actually spent in cycles).
/// Tonnes per calendar hour rates the fleet's use of the window: tonnes / truck-calendar-hours,
/// i.e. hours elapsed x the number of trucks in scope (CalendarScope, so a shift filter shrinks
/// the denominator along with the cycles) - NOT hours elapsed alone, which would silently divide
/// by wall-clock time instead of truck-hours and understate the rate by a factor of the fleet size.
/// </summary>
public static class TonnesPerHourCalculator
{
    public sealed record Result(
        decimal? TonnesPerOperatingHour,
        decimal? TonnesPerCalendarHour,
        decimal TotalTonnes,
        decimal OperatingHours,
        decimal CalendarHours);

    public static Result Calculate(IReadOnlyCollection<CycleRow> cycles, DateTime from, DateTime to, string? shift, int truckCount)
    {
        var totalTonnes = cycles.Sum(c => c.PayloadTonnes);
        var operatingHours = cycles.Sum(c => c.TotalCycleMin) / 60m;
        var calendarHours = CalendarScope.MinutesInScope(from, to, shift) / 60m * truckCount;

        var perOperating = operatingHours > 0 ? totalTonnes / operatingHours : (decimal?)null;
        var perCalendar = calendarHours > 0 ? totalTonnes / calendarHours : (decimal?)null;

        return new Result(perOperating, perCalendar, totalTonnes, operatingHours, calendarHours);
    }
}
