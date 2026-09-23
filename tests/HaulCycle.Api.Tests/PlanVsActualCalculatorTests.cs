using HaulCycle.Api.Kpi;

namespace HaulCycle.Api.Tests;

public class PlanVsActualCalculatorTests
{
    [Fact]
    public void NoSchedules_PlannedIsNullNotDivideByZero()
    {
        var cycles = new[] { TestData.Cycle(payloadTonnes: 500m) };

        var result = PlanVsActualCalculator.Calculate(cycles, []);

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

        var result = PlanVsActualCalculator.Calculate(cycles, schedules);

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

        var result = PlanVsActualCalculator.Calculate(cycles, schedules);

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

        var result = PlanVsActualCalculator.Calculate(cycles, schedules);

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

        var result = PlanVsActualCalculator.Calculate(cycles, []);

        var crusher = Assert.Single(result.ByDestination);
        Assert.Equal(200m, crusher.ActualTonnes);
        Assert.Null(crusher.PlannedTonnes);
        Assert.Null(crusher.PercentOfPlan);
    }
}
