namespace HaulCycle.Api.Kpi;

/// <summary>Shared shift-end date math: a Day shift (06:00-17:59) ends at 18:00 on
/// ShiftDate; a Night shift (18:00-05:59) ends at 06:00 the following day.</summary>
internal static class ShiftWindow
{
    public static DateTime End(DateOnly shiftDate, string shiftName) =>
        shiftName == "Day"
            ? shiftDate.ToDateTime(TimeOnly.MinValue).AddHours(18)
            : shiftDate.ToDateTime(TimeOnly.MinValue).AddDays(1).AddHours(6);
}
