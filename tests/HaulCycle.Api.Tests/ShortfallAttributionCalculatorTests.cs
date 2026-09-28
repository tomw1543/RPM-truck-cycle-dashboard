using HaulCycle.Api.Kpi;

namespace HaulCycle.Api.Tests;

public class ShortfallAttributionCalculatorTests
{
    private static readonly Dictionary<string, RouteTargetRow> Routes = new()
    {
        ["L1-ROM pad"] = new RouteTargetRow("L1-ROM pad", TargetCycleMin: 20m, TargetQueueMin: 0.8m, TargetLoadMin: 3.8m, TargetHaulMin: 10m, TargetDumpMin: 1.2m, TargetReturnMin: 4.2m),
    };

    private static readonly Dictionary<string, decimal> Capacity = new() { ["T01"] = 220m };

    private static readonly DateTime ShiftStart = new(2026, 9, 1, 6, 0, 0);
    private static readonly DateTime ShiftEnd = new(2026, 9, 1, 18, 0, 0);
    private static readonly DateTime AsOf = new(2026, 9, 2, 0, 0, 0); // shift complete

    [Fact]
    public void Calculate_BucketsSumExactlyToGap()
    {
        var schedule = TestData.Schedule("T01", plannedTonnes: 4400m, plannedCycles: 20m);
        var cycle = TestData.Cycle(
            truck: "T01", route: "L1-ROM pad",
            queueMin: 2m, loadMin: 4m, haulMin: 12m, dumpMin: 1m, returnMin: 4m,
            totalCycleMin: 23m, payloadTonnes: 200m, capacityTonnes: 220m);

        var result = ShortfallAttributionCalculator.Calculate(
            [cycle], [schedule], [], [], Routes, Capacity, AsOf);

        var shift = Assert.Single(result.Shifts);
        var truck = Assert.Single(shift.Trucks);

        Assert.Equal(4200m, truck.Gap); // 4400 planned - 200 actual
        AssertClose(truck.Gap, truck.Buckets.Total);

        // Cross-check the individual figures too, not just the total.
        Assert.Equal(20m, truck.Buckets.PayloadShort); // 220 - 200
        Assert.Equal(0m, truck.Buckets.QueueLoaderDelay); // no LoaderDelays given
        AssertClose(13.2m, truck.Buckets.QueueOverTrucking); // (2 - 0.8) x 11 t/min
        AssertClose(22m, truck.Buckets.HaulOverTarget); // (12 - 10) x 11
        AssertClose(-2.2m, truck.Buckets.OtherOverTarget); // load +0.2, dump -0.2, return -0.2 -> -0.2 x 11
        AssertClose(4147m, truck.Buckets.Residual); // availableMin 400 - cycle 23 = 377 x 11

        AssertClose(shift.Gap, shift.Buckets.Total);
        AssertClose(result.Window.Gap, result.Window.Buckets.Total);
    }

    [Fact]
    public void Calculate_FasterThanTarget_ProducesNegativeGainedBucket()
    {
        var schedule = TestData.Schedule("T01", plannedTonnes: 4400m, plannedCycles: 20m);
        // Loads faster than target (3.0 vs 3.8) - "gained" time, signed negative, not floored.
        var cycle = TestData.Cycle(
            truck: "T01", route: "L1-ROM pad",
            queueMin: 0.8m, loadMin: 3.0m, haulMin: 10m, dumpMin: 1.2m, returnMin: 4.2m,
            totalCycleMin: 19.2m, payloadTonnes: 220m, capacityTonnes: 220m);

        var result = ShortfallAttributionCalculator.Calculate(
            [cycle], [schedule], [], [], Routes, Capacity, AsOf);

        var truck = Assert.Single(Assert.Single(result.Shifts).Trucks);
        Assert.True(truck.Buckets.OtherOverTarget < 0m);
        AssertClose(truck.Gap, truck.Buckets.Total);
    }

    [Fact]
    public void Calculate_QueueOverTarget_LoaderDelayOverlapCapsAtQueueOverTarget()
    {
        var schedule = TestData.Schedule("T01", plannedTonnes: 4400m, plannedCycles: 20m);
        var start = new DateTime(2026, 9, 1, 8, 0, 0);
        // QueueMin 2 -> over target by 1.2 min. A loader delay spans the whole queue interval and
        // beyond, so raw overlap (2 min) exceeds queueOverTarget (1.2) - the split must cap there,
        // leaving nothing for over-trucking.
        var cycle = TestData.Cycle(
            truck: "T01", route: "L1-ROM pad", start: start,
            queueMin: 2m, loadMin: 3.8m, haulMin: 10m, dumpMin: 1.2m, returnMin: 4.2m,
            totalCycleMin: 21.2m, payloadTonnes: 220m, capacityTonnes: 220m);
        var loaderDelay = TestData.LoaderDelay("L1", start.AddMinutes(-5), start.AddMinutes(10));

        var result = ShortfallAttributionCalculator.Calculate(
            [cycle], [schedule], [], [loaderDelay], Routes, Capacity, AsOf);

        var truck = Assert.Single(Assert.Single(result.Shifts).Trucks);
        AssertClose(1.2m * 11m, truck.Buckets.QueueLoaderDelay); // capped at queueOverTarget (1.2 min) x rate
        Assert.Equal(0m, truck.Buckets.QueueOverTrucking);
        AssertClose(truck.Gap, truck.Buckets.Total);
    }

    [Fact]
    public void Calculate_UnplannedDowntime_ClippedToShiftWindow()
    {
        var schedule = TestData.Schedule("T01", plannedTonnes: 4400m, plannedCycles: 20m);
        // Starts before the shift, ends inside it - only the in-shift portion (2 hours = 120 min)
        // counts as unplanned downtime for this shift.
        var delay = TestData.Delay("T01", ShiftStart.AddHours(-1), ShiftStart.AddHours(2), "Breakdown", isPlanned: false);

        var result = ShortfallAttributionCalculator.Calculate(
            [], [schedule], [delay], [], Routes, Capacity, AsOf);

        var truck = Assert.Single(Assert.Single(result.Shifts).Trucks);
        AssertClose(120m * 11m, truck.Buckets.UnplannedDowntime);
        AssertClose(truck.Gap, truck.Buckets.Total);
    }

    [Fact]
    public void Calculate_UnavailableTruck_ExcludedFromDecomposition()
    {
        var schedule = TestData.Schedule("T02", unavailableReason: "Major breakdown");

        var result = ShortfallAttributionCalculator.Calculate(
            [], [schedule], [], [], Routes, Capacity, AsOf);

        Assert.Empty(result.Shifts);
    }

    private static void AssertClose(decimal expected, decimal actual) =>
        Assert.True(Math.Abs(expected - actual) < 0.01m, $"expected {expected}, got {actual}");
}
