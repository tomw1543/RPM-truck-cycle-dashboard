using HaulCycle.Api.Kpi;

namespace HaulCycle.Api.Tests;

public class CycleTimeCalculatorTests
{
    [Fact]
    public void EmptyWindow_ReturnsNullsNotDivideByZero()
    {
        var result = CycleTimeCalculator.Calculate([]);

        Assert.Null(result.AverageCycleMin);
        Assert.Null(result.PhaseSplit);
        Assert.Equal(0, result.CycleCount);
    }

    [Fact]
    public void ComputesAverageAndPhaseSplitSummingToOneHundred()
    {
        var cycles = new[]
        {
            TestData.Cycle(loadMin: 4m, haulMin: 12m, dumpMin: 1m, returnMin: 3m, queueMin: 0m), // total 20
            TestData.Cycle(loadMin: 4m, haulMin: 12m, dumpMin: 1m, returnMin: 3m, queueMin: 0m), // total 20
        };

        var result = CycleTimeCalculator.Calculate(cycles);

        Assert.Equal(20m, result.AverageCycleMin);
        Assert.NotNull(result.PhaseSplit);
        var split = result.PhaseSplit!;
        Assert.Equal(20m, split.LoadPercent);  // 4/20
        Assert.Equal(60m, split.HaulPercent);  // 12/20
        Assert.Equal(5m, split.DumpPercent);   // 1/20
        Assert.Equal(15m, split.ReturnPercent);// 3/20
        Assert.Equal(0m, split.QueuePercent);
        Assert.Equal(100m, split.LoadPercent + split.HaulPercent + split.DumpPercent + split.ReturnPercent + split.QueuePercent);
    }
}
