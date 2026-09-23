using HaulCycle.Api.Kpi;

namespace HaulCycle.Api.Tests;

public class MatchFactorCalculatorTests
{
    [Fact]
    public void EmptyCycles_ReturnsNull()
    {
        Assert.Null(MatchFactorCalculator.Calculate([], truckCount: 12, loaderCount: 3));
    }

    [Fact]
    public void ZeroLoadersOrTrucks_ReturnsNull()
    {
        var cycles = new[] { TestData.Cycle() };

        Assert.Null(MatchFactorCalculator.Calculate(cycles, truckCount: 0, loaderCount: 3));
        Assert.Null(MatchFactorCalculator.Calculate(cycles, truckCount: 12, loaderCount: 0));
    }

    [Fact]
    public void ComputesTrucksTimesLoadOverLoadersTimesCycle()
    {
        // avg load 4 min, avg total cycle 20 min, 12 trucks, 3 loaders
        // -> (12 * 4) / (3 * 20) = 48 / 60 = 0.8
        var cycles = new[]
        {
            TestData.Cycle(loadMin: 4m, totalCycleMin: 20m),
            TestData.Cycle(loadMin: 4m, totalCycleMin: 20m),
        };

        var result = MatchFactorCalculator.Calculate(cycles, truckCount: 12, loaderCount: 3);

        Assert.Equal(0.8m, result);
    }

    [Fact]
    public void BelowOne_LoadersWaitForTrucks_AboveOne_TrucksQueueForLoaders()
    {
        // Fewer trucks relative to loaders/cycle-time pushes the factor below 1; many more
        // trucks than loaders/cycle-time need pushes it above 1.
        var cycles = new[] { TestData.Cycle(loadMin: 2m, totalCycleMin: 3m) };

        var lowFactor = MatchFactorCalculator.Calculate(cycles, truckCount: 1, loaderCount: 3);
        var highFactor = MatchFactorCalculator.Calculate(cycles, truckCount: 100, loaderCount: 3);

        Assert.True(lowFactor < 1m);
        Assert.True(highFactor > 1m);
    }
}
