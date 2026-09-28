using HaulCycle.Api.Kpi;

namespace HaulCycle.Api.Tests;

public class LoaderShiftCalculatorTests
{
    private static readonly DateTime AsOf = new(2026, 9, 2, 0, 0, 0); // shift complete
    private static readonly string[] Loaders = ["L1", "L2"];

    [Fact]
    public void Calculate_TrucksComesFromSchedulesNotCycles()
    {
        var schedules = new[]
        {
            TestData.Schedule("T01"),
            TestData.Schedule("T02"),
        };

        var result = LoaderShiftCalculator.Calculate([], schedules, [], Loaders, AsOf);

        var l1 = result.Shifts.Single(s => s.LoaderName == "L1");
        Assert.Equal(2, l1.Trucks);
        Assert.Equal(0m, l1.LoadingMin); // no cycles yet
    }

    [Fact]
    public void Calculate_UtilisationAccountsForStoppedMinutes()
    {
        var schedules = new[] { TestData.Schedule("T01") };
        var cycles = new[]
        {
            TestData.Cycle(truck: "T01", loader: "L1", loadMin: 100m),
            TestData.Cycle(truck: "T01", loader: "L1", loadMin: 200m),
        };
        // A 15-minute shift-change handover (RateFactor 0.00) inside the shift.
        var loaderDelays = new[] { TestData.LoaderDelay("L1", new DateTime(2026, 9, 1, 6, 0, 0), new DateTime(2026, 9, 1, 6, 15, 0)) };

        var result = LoaderShiftCalculator.Calculate(cycles, schedules, loaderDelays, Loaders, AsOf);

        var l1 = result.Shifts.Single(s => s.LoaderName == "L1");
        Assert.Equal(300m, l1.LoadingMin);
        Assert.Equal(15m, l1.StoppedMin);
        Assert.Equal(300m / (720m - 15m), l1.Utilisation);
        Assert.True(l1.Utilisation < 1m);
    }

    [Fact]
    public void Calculate_MatchFactor_ZeroWithNoCycles()
    {
        var schedules = new[] { TestData.Schedule("T01") };
        var result = LoaderShiftCalculator.Calculate([], schedules, [], Loaders, AsOf);
        var l1 = result.Shifts.Single(s => s.LoaderName == "L1");
        Assert.Equal(0m, l1.MatchFactor);
    }

    [Fact]
    public void Calculate_MatchFactor_TrucksTimesAvgLoadOverAvgTotalCycle()
    {
        var schedules = new[] { TestData.Schedule("T01"), TestData.Schedule("T02") };
        var cycles = new[]
        {
            TestData.Cycle(truck: "T01", loader: "L1", loadMin: 4m, totalCycleMin: 20m),
            TestData.Cycle(truck: "T02", loader: "L1", loadMin: 4m, totalCycleMin: 20m),
        };

        var result = LoaderShiftCalculator.Calculate(cycles, schedules, [], Loaders, AsOf);
        var l1 = result.Shifts.Single(s => s.LoaderName == "L1");
        Assert.Equal(2 * 4m / 20m, l1.MatchFactor);
    }
}
