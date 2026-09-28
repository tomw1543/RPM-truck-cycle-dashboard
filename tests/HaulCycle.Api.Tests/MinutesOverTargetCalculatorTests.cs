using HaulCycle.Api.Kpi;

namespace HaulCycle.Api.Tests;

public class MinutesOverTargetCalculatorTests
{
    private static readonly Dictionary<string, RouteTargetRow> Routes = new()
    {
        ["L1-ROM pad"] = new RouteTargetRow("L1-ROM pad", TargetCycleMin: 20m, TargetQueueMin: 0.8m, TargetLoadMin: 3.8m, TargetHaulMin: 10m, TargetDumpMin: 1.2m, TargetReturnMin: 4.2m),
    };

    private static readonly Dictionary<string, decimal> Rates = new() { ["L1-ROM pad"] = 3m };

    [Fact]
    public void Calculate_UnknownRoute_Ignored()
    {
        var cycles = new[] { TestData.Cycle(route: "Unknown", totalCycleMin: 100m) };
        var result = MinutesOverTargetCalculator.Calculate(cycles, Routes, Rates);
        Assert.Equal(0m, result.TotalMin);
    }

    [Fact]
    public void Calculate_AtOrBelowTarget_ContributesNothing()
    {
        var cycles = new[] { TestData.Cycle(totalCycleMin: 20m) };
        var result = MinutesOverTargetCalculator.Calculate(cycles, Routes, Rates);
        Assert.Equal(0m, result.TotalMin);
    }

    [Fact]
    public void Calculate_OverTarget_AttributesAndConvertsToEquivalentTonnes()
    {
        var cycles = new[] { TestData.Cycle(queueMin: 0.8m, loadMin: 3.8m, haulMin: 15m, dumpMin: 1.2m, returnMin: 4.2m, totalCycleMin: 25m) };
        var result = MinutesOverTargetCalculator.Calculate(cycles, Routes, Rates);

        Assert.Equal(5m, result.TotalMin); // haul excess 5, everything else at target
        Assert.Equal(5m, result.ByPhase.Haul);
        Assert.Equal(0m, result.ByPhase.Queue + result.ByPhase.Load + result.ByPhase.Dump + result.ByPhase.Return + result.ByPhase.Unattributed);
        Assert.Equal(15m, result.TotalEquivalentTonnes); // 5 min x 3 t/min
        Assert.Single(result.ByRoute);
    }
}
