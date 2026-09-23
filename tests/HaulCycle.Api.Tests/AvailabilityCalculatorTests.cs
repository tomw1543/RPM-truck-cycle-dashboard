using HaulCycle.Api.Kpi;

namespace HaulCycle.Api.Tests;

public class AvailabilityCalculatorTests
{
    private static readonly DateTime Start = new(2026, 9, 1, 0, 0, 0);

    [Fact]
    public void EmptyWindow_ReturnsNullsNotDivideByZero()
    {
        var result = AvailabilityCalculator.Calculate(Start, Start, shift: null, truckCount: 12, cycles: [], delays: []);

        Assert.Null(result.Availability);
        Assert.Null(result.Utilisation);
        Assert.Null(result.EffectiveUtilisation);
        Assert.Equal(0m, result.CalendarMinutes);
        Assert.Equal(0m, result.IdleMinutes);
        Assert.Null(result.IdlePercent);
    }

    [Fact]
    public void NoTrucks_ReturnsNullsNotDivideByZero()
    {
        var to = Start.AddDays(1);
        var result = AvailabilityCalculator.Calculate(Start, to, shift: null, truckCount: 0, cycles: [], delays: []);

        Assert.Null(result.Availability);
        Assert.Null(result.Utilisation);
        Assert.Null(result.EffectiveUtilisation);
    }

    [Fact]
    public void TruckDownWholeWindow_AvailabilityAndEffectiveUtilisationAreZero()
    {
        var to = Start.AddHours(12);
        var delays = new[] { TestData.Delay("T01", Start, to) };

        var result = AvailabilityCalculator.Calculate(Start, to, shift: null, truckCount: 1, cycles: [], delays: delays);

        Assert.Equal(0m, result.Availability);
        Assert.Equal(0m, result.EffectiveUtilisation);
        // Available minutes is zero too, so utilisation (working / available) is null, not a divide-by-zero result.
        Assert.Null(result.Utilisation);
        Assert.Equal(0m, result.IdleMinutes); // no calendar time left over - all of it is delay
    }

    [Fact]
    public void QueueMinutesCountAsWorkingTime()
    {
        var to = Start.AddHours(1);
        // A single cycle whose queue time makes up most of the cycle - all of it should
        // still count as working time.
        var cycles = new[] { TestData.Cycle(start: Start, loadMin: 1m, haulMin: 1m, dumpMin: 1m, returnMin: 1m, queueMin: 20m) };

        var result = AvailabilityCalculator.Calculate(Start, to, shift: null, truckCount: 1, cycles: cycles, delays: []);

        Assert.Equal(24m, result.WorkingMinutes); // 1+1+1+1+20
    }

    [Fact]
    public void NormalWindow_ComputesAvailabilityUtilisationEffectiveUtilisation()
    {
        // 2 trucks, 10-hour window -> 1200 calendar minutes.
        var to = Start.AddHours(10);
        // Truck 2 has a 60-minute unplanned delay.
        var delays = new[] { TestData.Delay("T02", Start.AddHours(1), Start.AddHours(2)) };
        // 300 minutes of cycle (working) time across the fleet.
        var cycles = new[]
        {
            TestData.Cycle(start: Start, truck: "T01", totalCycleMin: 200m),
            TestData.Cycle(start: Start.AddHours(3), truck: "T02", totalCycleMin: 100m),
        };

        var result = AvailabilityCalculator.Calculate(Start, to, shift: null, truckCount: 2, cycles: cycles, delays: delays);

        Assert.Equal(1200m, result.CalendarMinutes);
        Assert.Equal(60m, result.DelayMinutes);
        Assert.Equal(1140m, result.AvailableMinutes);
        Assert.Equal(300m, result.WorkingMinutes);
        Assert.Equal(1140m / 1200m, result.Availability);
        Assert.Equal(300m / 1140m, result.Utilisation);
        Assert.Equal(300m / 1200m, result.EffectiveUtilisation);
        // Idle = calendar - working - delay = 1200 - 300 - 60 = 840.
        Assert.Equal(840m, result.IdleMinutes);
        Assert.Equal(840m / 1200m, result.IdlePercent);
    }

    [Fact]
    public void TonnesPerCalendarHour_DividesByTruckHours_NotWallClockHours()
    {
        // Fix 1 regression: calendar time must be truck-calendar-time (trucks x window),
        // not wall-clock window length alone - dividing by wall-clock hours understated
        // the rate by a factor of the fleet size.
        var to = Start.AddHours(24);
        var result = AvailabilityCalculator.Calculate(Start, to, shift: null, truckCount: 12, cycles: [], delays: []);

        // 24h window x 12 trucks = 17,280 calendar minutes, not 1,440.
        Assert.Equal(24m * 60m * 12m, result.CalendarMinutes);
    }

    [Fact]
    public void ShiftFilter_ShrinksCalendarMinutesToShiftHoursOnly()
    {
        // Fix 2 regression: with shift=Day over a 7-day window, calendar time must count
        // only the Day (06:00-18:00) hours in that window, not the full 168 wall-clock hours.
        var from = new DateTime(2026, 9, 1, 0, 0, 0);
        var to = from.AddDays(7);

        var dayResult = AvailabilityCalculator.Calculate(from, to, shift: "Day", truckCount: 1, cycles: [], delays: []);
        var nightResult = AvailabilityCalculator.Calculate(from, to, shift: "Night", truckCount: 1, cycles: [], delays: []);
        var unfilteredResult = AvailabilityCalculator.Calculate(from, to, shift: null, truckCount: 1, cycles: [], delays: []);

        Assert.Equal(7 * 12 * 60m, dayResult.CalendarMinutes);
        Assert.Equal(7 * 12 * 60m, nightResult.CalendarMinutes);
        Assert.Equal(dayResult.CalendarMinutes + nightResult.CalendarMinutes, unfilteredResult.CalendarMinutes);
    }

    [Fact]
    public void ShiftFilter_ClipsDelayMinutesToShiftHours()
    {
        // A delay spanning 05:00-19:00 (crossing both the 06:00 and 18:00 boundaries) should
        // only contribute its Day-hours portion (06:00-18:00 = 12h = 720 min) when shift=Day.
        var from = new DateTime(2026, 9, 1, 0, 0, 0);
        var to = from.AddDays(1);
        var delays = new[] { TestData.Delay("T01", from.AddHours(5), from.AddHours(19)) };

        var result = AvailabilityCalculator.Calculate(from, to, shift: "Day", truckCount: 1, cycles: [], delays: delays);

        Assert.Equal(720m, result.DelayMinutes);
    }
}
