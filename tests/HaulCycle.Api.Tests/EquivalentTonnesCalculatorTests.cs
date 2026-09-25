using HaulCycle.Api.Kpi;

namespace HaulCycle.Api.Tests;

public class EquivalentTonnesCalculatorTests
{
    [Fact]
    public void RatesByRoute_ComputesTonnesPerMinute()
    {
        var cycles = new[]
        {
            TestData.Cycle(route: "A", payloadTonnes: 200m, totalCycleMin: 20m),
            TestData.Cycle(route: "A", payloadTonnes: 200m, totalCycleMin: 20m),
        };

        var rates = EquivalentTonnesCalculator.RatesByRoute(cycles);

        Assert.Equal(10m, rates["A"]); // 400 t / 40 min
    }

    [Fact]
    public void RatesByRoute_RouteWithNoCycles_NoEntry()
    {
        var rates = EquivalentTonnesCalculator.RatesByRoute([]);
        Assert.Empty(rates);
    }

    [Fact]
    public void Convert_KnownRoute_MultipliesByRate()
    {
        var rates = new Dictionary<string, decimal> { ["A"] = 5m };
        Assert.Equal(50m, EquivalentTonnesCalculator.Convert(10m, "A", rates));
    }

    [Fact]
    public void Convert_UnknownRoute_ReturnsNull()
    {
        var rates = new Dictionary<string, decimal> { ["A"] = 5m };
        Assert.Null(EquivalentTonnesCalculator.Convert(10m, "B", rates));
    }
}
