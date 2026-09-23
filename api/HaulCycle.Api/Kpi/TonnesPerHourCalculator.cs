namespace HaulCycle.Api.Kpi;

/// <summary>
/// Tonnes per operating hour rates the truck (tonnes / hours actually spent in cycles).
/// Tonnes per calendar hour rates the shift/window (tonnes / hours elapsed).
/// </summary>
public static class TonnesPerHourCalculator
{
    public sealed record Result(
        decimal? TonnesPerOperatingHour,
        decimal? TonnesPerCalendarHour,
        decimal TotalTonnes,
        decimal OperatingHours,
        decimal CalendarHours);

    public static Result Calculate(IReadOnlyCollection<CycleRow> cycles, DateTime from, DateTime to)
    {
        var totalTonnes = cycles.Sum(c => c.PayloadTonnes);
        var operatingHours = cycles.Sum(c => c.TotalCycleMin) / 60m;
        var calendarHours = to > from ? (decimal)(to - from).TotalHours : 0m;

        var perOperating = operatingHours > 0 ? totalTonnes / operatingHours : (decimal?)null;
        var perCalendar = calendarHours > 0 ? totalTonnes / calendarHours : (decimal?)null;

        return new Result(perOperating, perCalendar, totalTonnes, operatingHours, calendarHours);
    }
}
