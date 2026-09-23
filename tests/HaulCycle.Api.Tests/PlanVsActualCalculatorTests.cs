using HaulCycle.Api.Kpi;

namespace HaulCycle.Api.Tests;

public class PlanVsActualCalculatorTests
{
    // Well after the default TestData shift (2026-09-01 Day, which ends 2026-09-01 18:00),
    // so it always counts as complete unless a test says otherwise.
    private static readonly DateTime AsOf = new(2026, 9, 5, 0, 0, 0);

    [Fact]
    public void NoSchedules_PlannedIsNullNotDivideByZero()
    {
        var cycles = new[] { TestData.Cycle(payloadTonnes: 500m) };

        var result = PlanVsActualCalculator.Calculate(cycles, [], AsOf);

        Assert.Equal(500m, result.ActualTonnes);
        Assert.Null(result.PlannedTonnes);
        Assert.Null(result.PercentOfPlan);
    }

    [Fact]
    public void AllSchedulesUnavailable_PlannedIsNullNotZero()
    {
        var cycles = new CycleRow[] { };
        var schedules = new[]
        {
            TestData.Schedule("T01", unavailableReason: "Major breakdown"),
            TestData.Schedule("T02", unavailableReason: "Major breakdown"),
        };

        var result = PlanVsActualCalculator.Calculate(cycles, schedules, AsOf);

        Assert.Null(result.PlannedTonnes);
        Assert.Null(result.PercentOfPlan);
    }

    [Fact]
    public void TruckUnavailable_ContributesNoPlan_RestOfFleetStillCounted()
    {
        // T01 was scheduled and ran; T02 was unavailable all shift (no plan, no cycles).
        var cycles = new[] { TestData.Cycle(truck: "T01", payloadTonnes: 800m) };
        var schedules = new[]
        {
            TestData.Schedule("T01", plannedTonnes: 1000m),
            TestData.Schedule("T02", unavailableReason: "Major breakdown"),
        };

        var result = PlanVsActualCalculator.Calculate(cycles, schedules, AsOf);

        Assert.Equal(800m, result.ActualTonnes);
        Assert.Equal(1000m, result.PlannedTonnes); // unavailable truck adds 0, not counted as missing plan
        Assert.Equal(0.8m, result.PercentOfPlan);
    }

    [Fact]
    public void BreaksDownByDestination()
    {
        var cycles = new[]
        {
            TestData.Cycle(truck: "T01", destination: "ROM pad", payloadTonnes: 600m),
            TestData.Cycle(truck: "T02", destination: "Waste dump", payloadTonnes: 300m),
        };
        var schedules = new[]
        {
            TestData.Schedule("T01", destination: "ROM pad", plannedTonnes: 700m),
            TestData.Schedule("T02", destination: "Waste dump", plannedTonnes: 500m),
        };

        var result = PlanVsActualCalculator.Calculate(cycles, schedules, AsOf);

        var romPad = Assert.Single(result.ByDestination, d => d.Destination == "ROM pad");
        Assert.Equal(600m, romPad.ActualTonnes);
        Assert.Equal(700m, romPad.PlannedTonnes);

        var wasteDump = Assert.Single(result.ByDestination, d => d.Destination == "Waste dump");
        Assert.Equal(300m, wasteDump.ActualTonnes);
        Assert.Equal(500m, wasteDump.PlannedTonnes);
    }

    [Fact]
    public void DestinationWithActualButNoPlan_HasNullPercent()
    {
        var cycles = new[] { TestData.Cycle(destination: "Crusher", payloadTonnes: 200m) };

        var result = PlanVsActualCalculator.Calculate(cycles, [], AsOf);

        var crusher = Assert.Single(result.ByDestination);
        Assert.Equal(200m, crusher.ActualTonnes);
        Assert.Null(crusher.PlannedTonnes);
        Assert.Null(crusher.PercentOfPlan);
    }

    [Fact]
    public void IncompleteShift_IsExcludedFromActualAndPlanned()
    {
        // Fix 4 regression: a shift still in progress as of asOf has full planned tonnes
        // committed but only partial actual tonnes so far - it must be dropped entirely,
        // not counted at a partial ratio.
        var completeShiftDate = new DateOnly(2026, 9, 1);
        var inProgressShiftDate = new DateOnly(2026, 9, 4); // Day shift, ends 2026-09-04 18:00.
        var asOf = new DateTime(2026, 9, 4, 10, 0, 0); // mid-way through the 9-4 Day shift

        var cycles = new[]
        {
            TestData.Cycle(truck: "T01", shiftDate: completeShiftDate, payloadTonnes: 800m),
            TestData.Cycle(truck: "T01", shiftDate: inProgressShiftDate, payloadTonnes: 200m),
        };
        var schedules = new[]
        {
            TestData.Schedule("T01", shiftDate: completeShiftDate, plannedTonnes: 1000m),
            TestData.Schedule("T01", shiftDate: inProgressShiftDate, plannedTonnes: 1000m),
        };

        var result = PlanVsActualCalculator.Calculate(cycles, schedules, asOf);

        Assert.Equal(800m, result.ActualTonnes);
        Assert.Equal(1000m, result.PlannedTonnes);
        Assert.Equal(0.8m, result.PercentOfPlan);
        Assert.True(result.CompleteShiftsOnly);
        Assert.Equal(1, result.ExcludedShiftCount);
    }

    [Fact]
    public void AllShiftsComplete_ExcludedShiftCountIsZero()
    {
        var cycles = new[] { TestData.Cycle(payloadTonnes: 500m) };
        var schedules = new[] { TestData.Schedule("T01", plannedTonnes: 600m) };

        var result = PlanVsActualCalculator.Calculate(cycles, schedules, AsOf);

        Assert.Equal(0, result.ExcludedShiftCount);
        Assert.True(result.CompleteShiftsOnly);
    }

    [Fact]
    public void IncompleteShift_ExcludedFromDestinationBreakdownToo()
    {
        var completeShiftDate = new DateOnly(2026, 9, 1);
        var inProgressShiftDate = new DateOnly(2026, 9, 4);
        var asOf = new DateTime(2026, 9, 4, 10, 0, 0);

        var cycles = new[]
        {
            TestData.Cycle(truck: "T01", shiftDate: completeShiftDate, destination: "ROM pad", payloadTonnes: 600m),
            TestData.Cycle(truck: "T01", shiftDate: inProgressShiftDate, destination: "ROM pad", payloadTonnes: 900m),
        };

        var result = PlanVsActualCalculator.Calculate(cycles, [], asOf);

        var romPad = Assert.Single(result.ByDestination);
        Assert.Equal(600m, romPad.ActualTonnes); // the 900t from the in-progress shift is excluded
    }
}
