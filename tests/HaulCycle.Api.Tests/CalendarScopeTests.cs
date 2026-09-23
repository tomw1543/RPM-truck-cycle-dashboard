using HaulCycle.Api.Kpi;

namespace HaulCycle.Api.Tests;

/// <summary>Covers the shared "calendar minutes in scope" helper directly (Fix 2): a window
/// with a shift filter, a window without one, and partial first/last days.</summary>
public class CalendarScopeTests
{
    [Fact]
    public void NoShiftFilter_ReturnsWholeWindow()
    {
        var from = new DateTime(2026, 9, 1, 0, 0, 0);
        var to = from.AddDays(7);

        var minutes = CalendarScope.MinutesInScope(from, to, shift: null);

        Assert.Equal(7 * 24 * 60m, minutes);
    }

    [Fact]
    public void ShiftFilter_OverFullDays_IsExactlyHalfTheWindow()
    {
        var from = new DateTime(2026, 9, 1, 0, 0, 0);
        var to = from.AddDays(7); // 7 whole days, so Day and Night hours are each exactly half.

        var day = CalendarScope.MinutesInScope(from, to, "Day");
        var night = CalendarScope.MinutesInScope(from, to, "Night");

        Assert.Equal(7 * 12 * 60m, day);
        Assert.Equal(7 * 12 * 60m, night);
        Assert.Equal(7 * 24 * 60m, day + night);
    }

    [Fact]
    public void PartialFirstDay_StartingMidDayShift_CountsOnlyRemainingDayHours()
    {
        // Window starts at 10:00 (4 hours into the 06:00-18:00 Day shift) and ends at 18:00
        // the same day - entirely inside one Day shift.
        var from = new DateTime(2026, 9, 1, 10, 0, 0);
        var to = new DateTime(2026, 9, 1, 18, 0, 0);

        var day = CalendarScope.MinutesInScope(from, to, "Day");
        var night = CalendarScope.MinutesInScope(from, to, "Night");

        Assert.Equal(8 * 60m, day); // 10:00-18:00
        Assert.Equal(0m, night);
    }

    [Fact]
    public void PartialLastDay_EndingMidNightShift_CountsOnlyElapsedNightHours()
    {
        // Window runs from 06:00 on day 1 to 02:00 on day 2 - ends 4 hours into the Night
        // shift that started at 18:00 on day 1.
        var from = new DateTime(2026, 9, 1, 6, 0, 0);
        var to = new DateTime(2026, 9, 2, 2, 0, 0);

        var day = CalendarScope.MinutesInScope(from, to, "Day");
        var night = CalendarScope.MinutesInScope(from, to, "Night");

        Assert.Equal(12 * 60m, day);  // full Day shift, 06:00-18:00
        Assert.Equal(8 * 60m, night); // 18:00-02:00
    }

    [Fact]
    public void WindowStartingMidNightShift_CreditsThePrecedingNightHoursCorrectly()
    {
        // Window starts at 02:00, mid-way through the Night shift that began at 18:00 the
        // previous day. Only the hours actually inside [from, to) should count.
        var from = new DateTime(2026, 9, 2, 2, 0, 0);
        var to = new DateTime(2026, 9, 2, 6, 0, 0);

        var night = CalendarScope.MinutesInScope(from, to, "Night");
        var day = CalendarScope.MinutesInScope(from, to, "Day");

        Assert.Equal(4 * 60m, night); // 02:00-06:00
        Assert.Equal(0m, day);
    }

    [Fact]
    public void OverlapMinutesInScope_ClipsAnIntervalToShiftHoursAndWindow()
    {
        var from = new DateTime(2026, 9, 1, 0, 0, 0);
        var to = from.AddDays(1);

        // A delay from 04:00 to 08:00 overlaps the Day shift (06:00-18:00) for only 2 hours.
        var overlap = CalendarScope.OverlapMinutesInScope(
            from.AddHours(4), from.AddHours(8), from, to, "Day");

        Assert.Equal(2 * 60m, overlap);
    }

    [Fact]
    public void OverlapMinutesInScope_OutsideWindow_IsZero()
    {
        var from = new DateTime(2026, 9, 1, 0, 0, 0);
        var to = from.AddDays(1);

        var overlap = CalendarScope.OverlapMinutesInScope(
            to.AddHours(1), to.AddHours(2), from, to, null);

        Assert.Equal(0m, overlap);
    }
}
