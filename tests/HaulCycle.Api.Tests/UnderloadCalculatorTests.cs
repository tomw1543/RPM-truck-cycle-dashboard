using HaulCycle.Api.Kpi;

namespace HaulCycle.Api.Tests;

public class UnderloadCalculatorTests
{
    [Fact]
    public void Calculate_FullPayload_ZeroUnderload()
    {
        // Single cycle in both windowCycles and allCycles: median baseline = its own payload%
        // (100.0), so baseline fill equals its own fill and underload is zero.
        var cycles = new[] { TestData.Cycle(payloadTonnes: 220m, capacityTonnes: 220m) };
        var result = UnderloadCalculator.Calculate(cycles, cycles);
        Assert.Equal(100m, result.BaselinePayloadPercent);
        Assert.Equal(0m, result.TotalTonnes);
        Assert.Equal(0m, result.ByTruck.Single().UnderloadTonnes);
    }

    [Fact]
    public void Calculate_PartialPayload_SumsUnderloadPerTruck()
    {
        // allCycles (fleet-wide, all data) sets the baseline: payload% = 90, 92, 94, 96 (capacity
        // 200t each -> payload 180/184/188/192t). Median of 4 values interpolates between the
        // 2nd and 3rd ranks (92, 94) at the midpoint = 93%. Baseline fill = 93% of 200t = 186t.
        var allCycles = new[]
        {
            TestData.Cycle(truck: "A", payloadTonnes: 180m, capacityTonnes: 200m),
            TestData.Cycle(truck: "B", payloadTonnes: 184m, capacityTonnes: 200m),
            TestData.Cycle(truck: "C", payloadTonnes: 188m, capacityTonnes: 200m),
            TestData.Cycle(truck: "D", payloadTonnes: 192m, capacityTonnes: 200m),
        };

        // windowCycles: T07 runs two light cycles (160t = 80%, 164t = 82%), T01 runs one at 190t
        // (95%, above baseline). Underload = max(0, 186 - payload):
        //   T07: (186 - 160) + (186 - 164) = 26 + 22 = 48
        //   T01: max(0, 186 - 190) = 0
        var windowCycles = new[]
        {
            TestData.Cycle(truck: "T07", payloadTonnes: 160m, capacityTonnes: 200m),
            TestData.Cycle(truck: "T07", payloadTonnes: 164m, capacityTonnes: 200m),
            TestData.Cycle(truck: "T01", payloadTonnes: 190m, capacityTonnes: 200m),
        };

        var result = UnderloadCalculator.Calculate(windowCycles, allCycles);

        Assert.Equal(93m, result.BaselinePayloadPercent);
        Assert.Equal(48m, result.TotalTonnes);
        Assert.Equal("T07", result.ByTruck[0].TruckName); // sorted descending by underload
        Assert.Equal(48m, result.ByTruck[0].UnderloadTonnes);
        Assert.Equal(2, result.ByTruck[0].Cycles);
        Assert.Equal("T01", result.ByTruck[1].TruckName);
        Assert.Equal(0m, result.ByTruck[1].UnderloadTonnes);
    }

    [Fact]
    public void Calculate_TruckExactlyAtBaseline_ZeroUnderload()
    {
        // allCycles median = 93% (same set as above). A window truck loading exactly 93% of a
        // 200t capacity (186t) sits exactly on the baseline: 186 - 186 = 0.
        var allCycles = new[]
        {
            TestData.Cycle(truck: "A", payloadTonnes: 180m, capacityTonnes: 200m),
            TestData.Cycle(truck: "B", payloadTonnes: 184m, capacityTonnes: 200m),
            TestData.Cycle(truck: "C", payloadTonnes: 188m, capacityTonnes: 200m),
            TestData.Cycle(truck: "D", payloadTonnes: 192m, capacityTonnes: 200m),
        };
        var windowCycles = new[] { TestData.Cycle(truck: "T05", payloadTonnes: 186m, capacityTonnes: 200m) };

        var result = UnderloadCalculator.Calculate(windowCycles, allCycles);

        Assert.Equal(93m, result.BaselinePayloadPercent);
        Assert.Equal(0m, result.ByTruck.Single().UnderloadTonnes);
    }

    [Fact]
    public void Calculate_TruckAboveBaseline_ZeroUnderload()
    {
        // Same 93% baseline. A truck loading 97% of a 200t capacity (194t) runs above the
        // fleet's typical fill, so it's floored at zero rather than going negative.
        var allCycles = new[]
        {
            TestData.Cycle(truck: "A", payloadTonnes: 180m, capacityTonnes: 200m),
            TestData.Cycle(truck: "B", payloadTonnes: 184m, capacityTonnes: 200m),
            TestData.Cycle(truck: "C", payloadTonnes: 188m, capacityTonnes: 200m),
            TestData.Cycle(truck: "D", payloadTonnes: 192m, capacityTonnes: 200m),
        };
        var windowCycles = new[] { TestData.Cycle(truck: "T11", payloadTonnes: 194m, capacityTonnes: 200m) };

        var result = UnderloadCalculator.Calculate(windowCycles, allCycles);

        Assert.Equal(93m, result.BaselinePayloadPercent);
        Assert.Equal(0m, result.ByTruck.Single().UnderloadTonnes);
    }

    [Fact]
    public void Calculate_T07StyleTruck_PositiveUnderloadAgainstHighFleetBaseline()
    {
        // allCycles: payload% = 96, 97, 98 (capacity 200t -> 192/194/196t). Median of 3 (odd
        // count) is the middle rank exactly: 97%. Baseline fill = 97% of 200t = 194t.
        var allCycles = new[]
        {
            TestData.Cycle(truck: "A", payloadTonnes: 192m, capacityTonnes: 200m),
            TestData.Cycle(truck: "B", payloadTonnes: 194m, capacityTonnes: 200m),
            TestData.Cycle(truck: "C", payloadTonnes: 196m, capacityTonnes: 200m),
        };
        // T07 runs at 80% (160t of 200t): underload = 194 - 160 = 34t.
        var windowCycles = new[] { TestData.Cycle(truck: "T07", payloadTonnes: 160m, capacityTonnes: 200m) };

        var result = UnderloadCalculator.Calculate(windowCycles, allCycles);

        Assert.Equal(97m, result.BaselinePayloadPercent);
        Assert.Equal(34m, result.TotalTonnes);
        Assert.Equal(34m, result.ByTruck.Single().UnderloadTonnes);
    }
}
