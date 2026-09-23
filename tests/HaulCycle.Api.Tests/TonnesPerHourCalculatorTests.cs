using HaulCycle.Api.Kpi;

namespace HaulCycle.Api.Tests;

public class TonnesPerHourCalculatorTests
{
    private static readonly DateTime Start = new(2026, 9, 1, 0, 0, 0);

    [Fact]
    public void EmptyWindow_ReturnsNullRatesNotDivideByZero()
    {
        var result = TonnesPerHourCalculator.Calculate([], Start, Start, shift: null, truckCount: 12);

        Assert.Null(result.TonnesPerOperatingHour);
        Assert.Null(result.TonnesPerCalendarHour);
        Assert.Equal(0m, result.TotalTonnes);
    }

    [Fact]
    public void NoCycles_OperatingHourRateIsNullEvenWithACalendarWindow()
    {
        var to = Start.AddHours(10);
        var result = TonnesPerHourCalculator.Calculate([], Start, to, shift: null, truckCount: 1);

        Assert.Null(result.TonnesPerOperatingHour);
        Assert.Equal(0m, result.TonnesPerCalendarHour); // 0 tonnes / 10h = 0, a real rate, not undefined
    }

    [Fact]
    public void ComputesBothBases()
    {
        var to = Start.AddHours(24);
        var cycles = new[]
        {
            TestData.Cycle(start: Start, payloadTonnes: 1000m, totalCycleMin: 300m),
            TestData.Cycle(start: Start.AddHours(1), payloadTonnes: 1000m, totalCycleMin: 300m),
        };

        var result = TonnesPerHourCalculator.Calculate(cycles, Start, to, shift: null, truckCount: 1);

        Assert.Equal(2000m, result.TotalTonnes);
        Assert.Equal(10m, result.OperatingHours); // 600 minutes
        Assert.Equal(24m, result.CalendarHours); // 24h x 1 truck
        Assert.Equal(200m, result.TonnesPerOperatingHour); // 2000 / 10
        Assert.Equal(2000m / 24m, result.TonnesPerCalendarHour);
    }

    [Fact]
    public void TonnesPerCalendarHour_DividesByTruckHours_NotWallClockHoursAlone()
    {
        // Fix 1 regression: for a 7-day (168h) window with 12 trucks, calendar hours must be
        // 168 x 12 = 2016 truck-hours, not 168 wall-clock hours - the original bug divided by
        // wall-clock hours only, overstating the rate by a factor of the fleet size (12x here).
        var to = Start.AddDays(7);
        var cycles = new[] { TestData.Cycle(start: Start, payloadTonnes: 865_492m, totalCycleMin: 20m) };

        var result = TonnesPerHourCalculator.Calculate(cycles, Start, to, shift: null, truckCount: 12);

        Assert.Equal(168m * 12m, result.CalendarHours);
        Assert.Equal(865_492m / (168m * 12m), result.TonnesPerCalendarHour);
    }

    [Fact]
    public void ShiftFilter_ShrinksCalendarHoursToShiftHoursOnly()
    {
        // Fix 2 regression: with shift=Day, calendar hours must be the Day-only hours in the
        // window (half of it, for a whole-day window), not the full window length.
        var to = Start.AddDays(7);

        var dayResult = TonnesPerHourCalculator.Calculate([], Start, to, shift: "Day", truckCount: 1);
        var unfilteredResult = TonnesPerHourCalculator.Calculate([], Start, to, shift: null, truckCount: 1);

        Assert.Equal(unfilteredResult.CalendarHours / 2m, dayResult.CalendarHours);
    }
}
