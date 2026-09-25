using HaulCycle.Api.Kpi;

namespace HaulCycle.Api.Tests;

public class RecoverableMinutesCalculatorTests
{
    private static readonly Dictionary<string, decimal> OneRate = new() { ["L1-ROM pad"] = 2m };

    [Fact]
    public void Calculate_NoBenchmarkForRoute_CycleIgnored()
    {
        var cycles = new[] { TestData.Cycle(route: "Unknown route", totalCycleMin: 100m) };
        var benchmarks = new Dictionary<string, RouteBenchmarkCalculator.RouteBenchmark>();

        var result = RecoverableMinutesCalculator.Calculate(cycles, benchmarks, OneRate);

        Assert.Equal(0m, result.TotalMin);
        Assert.Empty(result.ByRoute);
    }

    [Fact]
    public void Calculate_CycleAtOrBelowBenchmark_ContributesNothing()
    {
        var cycles = new[] { TestData.Cycle(totalCycleMin: 10m) };
        var benchmarks = new Dictionary<string, RouteBenchmarkCalculator.RouteBenchmark>
        {
            ["L1-ROM pad"] = new(10m, new RouteBenchmarkCalculator.PhaseBenchmark(1m, 1m, 1m, 1m, 1m)),
        };

        var result = RecoverableMinutesCalculator.Calculate(cycles, benchmarks, OneRate);

        Assert.Equal(0m, result.TotalMin);
    }

    [Fact]
    public void Calculate_GapSplitsAcrossPhasesAndSumsToTotal()
    {
        // Cycle: queue 3, load 1, haul 20, dump 1, return 1 = 26. Benchmark total 20, phase refs
        // queue 0.8, load 3.8, haul 15, dump 1.2, return 4 -> gap 6, haul excess dominates.
        var cycles = new[] { TestData.Cycle(queueMin: 3m, loadMin: 1m, haulMin: 20m, dumpMin: 1m, returnMin: 1m, totalCycleMin: 26m) };
        var benchmarks = new Dictionary<string, RouteBenchmarkCalculator.RouteBenchmark>
        {
            ["L1-ROM pad"] = new(20m, new RouteBenchmarkCalculator.PhaseBenchmark(0.8m, 3.8m, 15m, 1.2m, 4m)),
        };

        var result = RecoverableMinutesCalculator.Calculate(cycles, benchmarks, OneRate);

        Assert.Equal(6m, result.TotalMin);
        Assert.Equal(6m, result.ByPhase.Total);
        Assert.Single(result.ByRoute);
        Assert.Equal(6m, result.ByRoute[0].Minutes);
        Assert.Equal(12m, result.ByRoute[0].EquivalentTonnes); // rate 2 t/min x 6 min
        Assert.Equal(6m, result.ByTruck.Single().Minutes);
        Assert.Equal(6m, result.ByLoader.Single().Minutes);
        Assert.Equal(result.TotalEquivalentTonnes, result.ByRoute[0].EquivalentTonnes);

        var routePhase = Assert.Single(result.ByRoutePhase);
        Assert.Equal(6m, routePhase.Phases.Total);
    }

    [Fact]
    public void Calculate_MultipleCycles_AggregatesPerRouteTruckLoader()
    {
        var cycles = new[]
        {
            TestData.Cycle(truck: "T01", loader: "L1", route: "L1-ROM pad", totalCycleMin: 30m),
            TestData.Cycle(truck: "T02", loader: "L1", route: "L1-ROM pad", totalCycleMin: 25m),
        };
        var benchmarks = new Dictionary<string, RouteBenchmarkCalculator.RouteBenchmark>
        {
            ["L1-ROM pad"] = new(20m, new RouteBenchmarkCalculator.PhaseBenchmark(0.8m, 3.8m, 15m, 1.2m, 4m)),
        };

        var result = RecoverableMinutesCalculator.Calculate(cycles, benchmarks, OneRate);

        Assert.Equal(15m, result.TotalMin); // 10 + 5
        Assert.Equal(2, result.ByTruck.Count);
        Assert.Equal(15m, result.ByRoute.Single().Minutes);
    }
}
