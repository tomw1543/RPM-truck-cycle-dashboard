using HaulCycle.Api.Kpi;

namespace HaulCycle.Api.Tests;

public class AvailabilityCalculatorTests
{
    private static readonly DateTime Start = new(2026, 9, 1, 0, 0, 0);

    [Fact]
    public void EmptyWindow_ReturnsNullsNotDivideByZero()
    {
        var result = AvailabilityCalculator.Calculate(Start, Start, truckCount: 12, cycles: [], delays: []);

        Assert.Null(result.Availability);
        Assert.Null(result.Utilisation);
        Assert.Null(result.EffectiveUtilisation);
        Assert.Equal(0m, result.CalendarMinutes);
    }

    [Fact]
    public void NoTrucks_ReturnsNullsNotDivideByZero()
    {
        var to = Start.AddDays(1);
        var result = AvailabilityCalculator.Calculate(Start, to, truckCount: 0, cycles: [], delays: []);

        Assert.Null(result.Availability);
        Assert.Null(result.Utilisation);
        Assert.Null(result.EffectiveUtilisation);
    }

    [Fact]
    public void TruckDownWholeWindow_AvailabilityAndEffectiveUtilisationAreZero()
    {
        var to = Start.AddHours(12);
        var delays = new[] { TestData.Delay("T01", Start, to) };

        var result = AvailabilityCalculator.Calculate(Start, to, truckCount: 1, cycles: [], delays: delays);

        Assert.Equal(0m, result.Availability);
        Assert.Equal(0m, result.EffectiveUtilisation);
        // Available minutes is zero too, so utilisation (working / available) is null, not a divide-by-zero result.
        Assert.Null(result.Utilisation);
    }

    [Fact]
    public void QueueMinutesCountAsWorkingTime()
    {
        var to = Start.AddHours(1);
        // A single cycle whose queue time makes up most of the cycle - all of it should
        // still count as working time.
        var cycles = new[] { TestData.Cycle(start: Start, loadMin: 1m, haulMin: 1m, dumpMin: 1m, returnMin: 1m, queueMin: 20m) };

        var result = AvailabilityCalculator.Calculate(Start, to, truckCount: 1, cycles: cycles, delays: []);

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

        var result = AvailabilityCalculator.Calculate(Start, to, truckCount: 2, cycles: cycles, delays: delays);

        Assert.Equal(1200m, result.CalendarMinutes);
        Assert.Equal(60m, result.DelayMinutes);
        Assert.Equal(1140m, result.AvailableMinutes);
        Assert.Equal(300m, result.WorkingMinutes);
        Assert.Equal(1140m / 1200m, result.Availability);
        Assert.Equal(300m / 1140m, result.Utilisation);
        Assert.Equal(300m / 1200m, result.EffectiveUtilisation);
    }
}
