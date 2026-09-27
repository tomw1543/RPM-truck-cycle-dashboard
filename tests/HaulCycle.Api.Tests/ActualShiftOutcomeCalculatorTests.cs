using HaulCycle.Api.Kpi;

namespace HaulCycle.Api.Tests;

public class ActualShiftOutcomeCalculatorTests
{
    [Fact]
    public void SplitsTonnesByDestinationName()
    {
        var cycles = new[]
        {
            TestData.Cycle(destination: "Crusher", material: "Ore", payloadTonnes: 200m, queueMin: 1m, loadMin: 3m, haulMin: 5m, dumpMin: 1m, returnMin: 4m),
            TestData.Cycle(destination: "ROM pad", material: "Ore", payloadTonnes: 150m, queueMin: 2m, loadMin: 3m, haulMin: 5m, dumpMin: 1m, returnMin: 4m),
            TestData.Cycle(destination: "Waste dump", material: "Waste", payloadTonnes: 100m, queueMin: 3m, loadMin: 3m, haulMin: 5m, dumpMin: 1m, returnMin: 4m),
        };

        var result = ActualShiftOutcomeCalculator.Calculate(cycles);

        Assert.Equal(200m, result.CrusherTonnes);
        Assert.Equal(150m, result.RomTonnes);
        Assert.Equal(100m, result.WasteTonnes);
        Assert.Equal(450m, result.TotalTonnes);
        Assert.Equal(3, result.Cycles);
        Assert.Equal(0.1m, result.QueueHours); // (1+2+3)/60
    }

    [Fact]
    public void NoCycles_AllZero()
    {
        var result = ActualShiftOutcomeCalculator.Calculate(Array.Empty<CycleRow>());

        Assert.Equal(0m, result.TotalTonnes);
        Assert.Equal(0, result.Cycles);
        Assert.Equal(0m, result.QueueHours);
        Assert.Equal(0m, result.FuelLitres);
    }
}
