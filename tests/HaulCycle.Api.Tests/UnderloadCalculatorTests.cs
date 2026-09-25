using HaulCycle.Api.Kpi;

namespace HaulCycle.Api.Tests;

public class UnderloadCalculatorTests
{
    [Fact]
    public void Calculate_FullPayload_ZeroUnderload()
    {
        var cycles = new[] { TestData.Cycle(payloadTonnes: 220m, capacityTonnes: 220m) };
        var result = UnderloadCalculator.Calculate(cycles);
        Assert.Equal(0m, result.TotalTonnes);
        Assert.Equal(0m, result.ByTruck.Single().UnderloadTonnes);
    }

    [Fact]
    public void Calculate_PartialPayload_SumsUnderloadPerTruck()
    {
        var cycles = new[]
        {
            TestData.Cycle(truck: "T07", payloadTonnes: 176m, capacityTonnes: 220m), // 44 t underload
            TestData.Cycle(truck: "T07", payloadTonnes: 180m, capacityTonnes: 220m), // 40 t underload
            TestData.Cycle(truck: "T01", payloadTonnes: 213m, capacityTonnes: 220m), // 7 t underload
        };

        var result = UnderloadCalculator.Calculate(cycles);

        Assert.Equal(91m, result.TotalTonnes);
        Assert.Equal("T07", result.ByTruck[0].TruckName); // sorted descending by underload
        Assert.Equal(84m, result.ByTruck[0].UnderloadTonnes);
        Assert.Equal(2, result.ByTruck[0].Cycles);
    }
}
