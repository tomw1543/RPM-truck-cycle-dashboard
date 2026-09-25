using HaulCycle.Api.Kpi;

namespace HaulCycle.Api.Tests;

public class PhaseAttributionCalculatorTests
{
    [Fact]
    public void Attribute_ZeroGap_ReturnsZero()
    {
        var result = PhaseAttributionCalculator.Attribute(0m, 5m, 5m, 5m, 5m, 5m, 1m, 1m, 1m, 1m, 1m);
        Assert.Equal(0m, result.Total);
    }

    [Fact]
    public void Attribute_NoPhaseExcess_GoesToUnattributed()
    {
        // Every phase is at or below its reference, but the total is over (percentiles don't add).
        var result = PhaseAttributionCalculator.Attribute(3m, 1m, 1m, 1m, 1m, 1m, 1m, 1m, 1m, 1m, 1m);
        Assert.Equal(3m, result.Unattributed);
        Assert.Equal(0m, result.Queue + result.Load + result.Haul + result.Dump + result.Return);
        Assert.Equal(3m, result.Total);
    }

    [Fact]
    public void Attribute_SharesProportionalToExcess()
    {
        // Excesses: queue 2, haul 6, others 0. Sum = 8. Gap = 4 -> queue 1, haul 3.
        var result = PhaseAttributionCalculator.Attribute(
            gap: 4m,
            queueMin: 3m, loadMin: 1m, haulMin: 16m, dumpMin: 1m, returnMin: 1m,
            refQueueMin: 1m, refLoadMin: 1m, refHaulMin: 10m, refDumpMin: 1m, refReturnMin: 1m);

        Assert.Equal(1m, result.Queue);
        Assert.Equal(0m, result.Load);
        Assert.Equal(3m, result.Haul);
        Assert.Equal(0m, result.Dump);
        Assert.Equal(0m, result.Return);
        Assert.Equal(0m, result.Unattributed);
        Assert.Equal(4m, result.Total);
    }

    [Fact]
    public void Attribute_NullReference_TreatsExcessAsZero()
    {
        var result = PhaseAttributionCalculator.Attribute(
            gap: 2m,
            queueMin: 5m, loadMin: 1m, haulMin: 1m, dumpMin: 1m, returnMin: 1m,
            refQueueMin: null, refLoadMin: 1m, refHaulMin: 1m, refDumpMin: 1m, refReturnMin: 1m);

        // Queue has no reference, so its excess is 0 even though it's numerically the largest phase -
        // with every other phase also at its reference, the whole gap is unattributed.
        Assert.Equal(0m, result.Queue);
        Assert.Equal(2m, result.Unattributed);
    }

    [Fact]
    public void Attribute_SharesAlwaysSumToGap()
    {
        var result = PhaseAttributionCalculator.Attribute(
            gap: 7.3m,
            queueMin: 2.1m, loadMin: 5.9m, haulMin: 20.4m, dumpMin: 1.5m, returnMin: 6.2m,
            refQueueMin: 0.8m, refLoadMin: 3.8m, refHaulMin: 15m, refDumpMin: 1.2m, refReturnMin: 4m);

        Assert.Equal(7.3m, result.Total);
    }
}
