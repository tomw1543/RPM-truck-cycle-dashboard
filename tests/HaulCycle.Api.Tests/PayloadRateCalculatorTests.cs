using HaulCycle.Api.Kpi;

namespace HaulCycle.Api.Tests;

public class PayloadRateCalculatorTests
{
    [Fact]
    public void NoCycles_ReturnsNullsNotDivideByZero()
    {
        var result = PayloadRateCalculator.Calculate([]);

        Assert.Null(result.AveragePayloadPercent);
        Assert.Null(result.CyclesPerOperatingHour);
    }

    [Fact]
    public void ComputesAveragePayloadPercentAndCyclesPerOperatingHour()
    {
        var cycles = new[]
        {
            TestData.Cycle(payloadTonnes: 220m, capacityTonnes: 220m, totalCycleMin: 30m), // 100%
            TestData.Cycle(payloadTonnes: 176m, capacityTonnes: 220m, totalCycleMin: 30m), // 80%
        };

        var result = PayloadRateCalculator.Calculate(cycles);

        Assert.Equal(90m, result.AveragePayloadPercent); // (100 + 80) / 2
        // 2 cycles / (60 minutes / 60) = 2 cycles per operating hour.
        Assert.Equal(2m, result.CyclesPerOperatingHour);
    }

    [Fact]
    public void UnderloadedTruck_HasLowerPayloadPercentAndHigherCyclesPerHour()
    {
        // Regression guard for the T07 planted problem: underloading (lower payload share)
        // means shorter load times, so more cycles fit in the same operating hour.
        var normal = new[]
        {
            TestData.Cycle(payloadTonnes: 213m, capacityTonnes: 220m, loadMin: 3.8m, haulMin: 10m, dumpMin: 1.2m, returnMin: 4m, queueMin: 0.8m),
        };
        var underloaded = new[]
        {
            TestData.Cycle(payloadTonnes: 176m, capacityTonnes: 220m, loadMin: 3.1m, haulMin: 10m, dumpMin: 1.2m, returnMin: 4m, queueMin: 0.8m),
        };

        var normalResult = PayloadRateCalculator.Calculate(normal);
        var underloadedResult = PayloadRateCalculator.Calculate(underloaded);

        Assert.True(underloadedResult.AveragePayloadPercent < normalResult.AveragePayloadPercent);
        Assert.True(underloadedResult.CyclesPerOperatingHour > normalResult.CyclesPerOperatingHour);
    }
}
