using HaulCycle.Api.Kpi;

namespace HaulCycle.Api.Tests;

public class BiggestLossesCalculatorTests
{
    [Fact]
    public void Calculate_RanksByEquivalentTonnesDescending_AndCapsAtTen()
    {
        var rates = new Dictionary<string, decimal> { ["A"] = 1m };

        var overTarget = new MinutesOverTargetCalculator.Result(
            TotalMin: 10m, TotalEquivalentTonnes: 10m, ByPhase: PhaseAttributionCalculator.Zero,
            ByRoute:
            [
                new MinutesOverTargetCalculator.RoutePhaseMinutes(
                    "A", 10m, 10m,
                    new PhaseAttributionCalculator.PhaseAmounts(Queue: 2m, Load: 0m, Haul: 8m, Dump: 0m, Return: 0m, Unattributed: 0m)),
            ]);

        var underload = new UnderloadCalculator.Result(
            50m,
            [new UnderloadCalculator.TruckUnderload("T07", 10, 50m, 80m)],
            97m);

        var items = BiggestLossesCalculator.Calculate(overTarget, underload, rates);

        Assert.Equal(3, items.Count); // queue item (2t), haul item (8t), underload item (50t)
        Assert.Equal(BiggestLossesCalculator.MeasureUnderload, items[0].Measure); // 50t ranks first
        Assert.Equal("T07", items[0].Subject);
        Assert.Equal(50m, items[0].EquivalentTonnes);
        Assert.Equal(BiggestLossesCalculator.MeasureOverTarget, items[1].Measure); // haul 8t
        Assert.Equal("Haul", items[1].Phase);
    }

    [Fact]
    public void Calculate_RouteWithNoRate_ExcludesMinuteItems()
    {
        var overTarget = new MinutesOverTargetCalculator.Result(
            10m, 0m, PhaseAttributionCalculator.Zero,
            [new MinutesOverTargetCalculator.RoutePhaseMinutes("Unrated", 10m, null,
                new PhaseAttributionCalculator.PhaseAmounts(10m, 0m, 0m, 0m, 0m, 0m))]);
        var underload = new UnderloadCalculator.Result(0m, [], 97m);

        var items = BiggestLossesCalculator.Calculate(overTarget, underload, new Dictionary<string, decimal>());

        Assert.Empty(items);
    }
}
