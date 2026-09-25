using HaulCycle.Api.Kpi;

namespace HaulCycle.Api.Tests;

public class RouteBenchmarkCalculatorTests
{
    [Fact]
    public void Percentile25_EmptyInput_ReturnsNull()
    {
        Assert.Null(RouteBenchmarkCalculator.Percentile25([]));
    }

    [Fact]
    public void Percentile25_SingleValue_ReturnsThatValue()
    {
        Assert.Equal(42m, RouteBenchmarkCalculator.Percentile25([42m]));
    }

    [Fact]
    public void Percentile25_OddCount_MatchesHandComputedValue()
    {
        // Sorted: 1,2,3,4,5 (n=5). Rank = 0.25 * 4 = 1.0 -> index 1 -> value 2.
        var values = new List<decimal> { 5m, 1m, 4m, 2m, 3m };
        Assert.Equal(2m, RouteBenchmarkCalculator.Percentile25(values));
    }

    [Fact]
    public void Percentile25_EvenCount_InterpolatesBetweenRanks()
    {
        // Sorted: 1,2,3,4 (n=4). Rank = 0.25 * 3 = 0.75 -> between index 0 (1) and 1 (2),
        // fraction 0.75 -> 1 + (2-1)*0.75 = 1.75.
        var values = new List<decimal> { 4m, 2m, 1m, 3m };
        Assert.Equal(1.75m, RouteBenchmarkCalculator.Percentile25(values));
    }

    [Fact]
    public void Percentile25_TiedValues_ReturnsTheTiedValue()
    {
        var values = new List<decimal> { 5m, 5m, 5m, 5m };
        Assert.Equal(5m, RouteBenchmarkCalculator.Percentile25(values));
    }

    [Fact]
    public void Calculate_EmptyInput_ReturnsEmptyDictionary()
    {
        var result = RouteBenchmarkCalculator.Calculate([]);
        Assert.Empty(result);
    }

    [Fact]
    public void Calculate_PerRouteAggregation_IsIndependentPerRoute()
    {
        var routeACycles = new[]
        {
            TestData.Cycle(route: "A", totalCycleMin: 10m, queueMin: 1m, loadMin: 2m, haulMin: 3m, dumpMin: 2m, returnMin: 2m),
            TestData.Cycle(route: "A", totalCycleMin: 20m, queueMin: 2m, loadMin: 4m, haulMin: 6m, dumpMin: 4m, returnMin: 4m),
        };
        var routeBCycles = new[]
        {
            TestData.Cycle(route: "B", totalCycleMin: 100m, queueMin: 10m, loadMin: 20m, haulMin: 30m, dumpMin: 20m, returnMin: 20m),
        };

        var all = routeACycles.Concat(routeBCycles).ToList();
        var result = RouteBenchmarkCalculator.Calculate(all);

        Assert.Equal(2, result.Count);
        // Route A: n=2, rank = 0.25*1 = 0.25 -> 10 + (20-10)*0.25 = 12.5.
        Assert.Equal(12.5m, result["A"].TotalCycleMin);
        // Route B: n=1 -> its own value.
        Assert.Equal(100m, result["B"].TotalCycleMin);
    }

    [Fact]
    public void Calculate_ZeroCycleRoute_HasNoEntry()
    {
        var cycles = new[] { TestData.Cycle(route: "A") };
        var result = RouteBenchmarkCalculator.Calculate(cycles);

        Assert.True(result.ContainsKey("A"));
        Assert.False(result.ContainsKey("ZeroCycleRoute"));
    }

    [Fact]
    public void Calculate_BenchmarkIsIndependentOfWhichCyclesAreInAWindow()
    {
        // The calculator itself only ever sees "all data" - this test documents that
        // calling it with a different subset yields a different benchmark, which is exactly
        // why callers must always pass ALL cycles (never a window-filtered subset) so the
        // benchmark stays stable across different requested windows.
        var allCycles = new[]
        {
            TestData.Cycle(route: "A", totalCycleMin: 10m),
            TestData.Cycle(route: "A", totalCycleMin: 20m),
            TestData.Cycle(route: "A", totalCycleMin: 30m),
            TestData.Cycle(route: "A", totalCycleMin: 40m),
        };
        var windowSubset = allCycles.Take(1).ToList();

        var fullBenchmark = RouteBenchmarkCalculator.Calculate(allCycles)["A"].TotalCycleMin;
        var subsetBenchmark = RouteBenchmarkCalculator.Calculate(windowSubset)["A"].TotalCycleMin;

        Assert.NotEqual(fullBenchmark, subsetBenchmark);
        Assert.Equal(17.5m, fullBenchmark); // rank = 0.25*3 = 0.75 -> 10 + (20-10)*0.75 = 17.5
        Assert.Equal(10m, subsetBenchmark);
    }
}
