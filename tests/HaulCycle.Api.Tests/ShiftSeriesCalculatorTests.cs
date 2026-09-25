using HaulCycle.Api.Kpi;

namespace HaulCycle.Api.Tests;

public class ShiftSeriesCalculatorTests
{
    // After every shift date used below (2026-9-1 / 2026-9-2), so existing tests' shifts are
    // all complete and their assertions don't need to account for IsComplete.
    private static readonly DateTime AsOfAfterAllShifts = new(2026, 9, 5);

    [Fact]
    public void NoCyclesNoSchedules_ReturnsEmpty()
    {
        var result = ShiftSeriesCalculator.Calculate([], [], AsOfAfterAllShifts);

        Assert.Empty(result);
    }

    [Fact]
    public void TruckWithNoCyclesThisWindow_StillAppearsForEachScheduledShift()
    {
        var schedules = new[]
        {
            TestData.Schedule("T01", shiftDate: new DateOnly(2026, 9, 1), shiftName: "Day", plannedTonnes: 1000m),
            TestData.Schedule("T01", shiftDate: new DateOnly(2026, 9, 1), shiftName: "Night", plannedTonnes: 900m),
        };

        var result = ShiftSeriesCalculator.Calculate([], schedules, AsOfAfterAllShifts);

        Assert.Equal(2, result.Count);
        Assert.All(result, r => Assert.Equal(0, r.Cycles));
        Assert.All(result, r => Assert.Equal(0m, r.ActualTonnes));
        Assert.All(result, r => Assert.Null(r.AveragePayloadPercent));
        Assert.All(result, r => Assert.True(r.IsComplete));
        // Ordered chronologically: Day (06:00) before Night (18:00) on the same date.
        Assert.Equal("Day", result[0].ShiftName);
        Assert.Equal("Night", result[1].ShiftName);
    }

    [Fact]
    public void UnavailableShift_HasReasonAndNoRouteOrCycles()
    {
        var schedules = new[]
        {
            TestData.Schedule("T01", unavailableReason: "Major breakdown", shiftDate: new DateOnly(2026, 9, 1)),
        };

        var result = ShiftSeriesCalculator.Calculate([], schedules, AsOfAfterAllShifts);

        var row = Assert.Single(result);
        Assert.Equal("Major breakdown", row.UnavailableReason);
        Assert.Null(row.RouteName);
        Assert.Null(row.PlannedTonnes);
        Assert.Equal(0, row.Cycles);
        Assert.Equal(0m, row.ActualTonnes);
    }

    [Fact]
    public void ComputesActualTonnesCyclesAndAveragePayloadPercentPerShift()
    {
        var shiftDate = new DateOnly(2026, 9, 1);
        var cycles = new[]
        {
            TestData.Cycle(shiftDate: shiftDate, shiftName: "Day", payloadTonnes: 200m, capacityTonnes: 220m),
            TestData.Cycle(shiftDate: shiftDate, shiftName: "Day", payloadTonnes: 220m, capacityTonnes: 220m),
        };
        var schedules = new[]
        {
            TestData.Schedule("T01", shiftDate: shiftDate, shiftName: "Day", plannedTonnes: 500m),
        };

        var result = ShiftSeriesCalculator.Calculate(cycles, schedules, AsOfAfterAllShifts);

        var row = Assert.Single(result);
        Assert.Equal(420m, row.ActualTonnes);
        Assert.Equal(500m, row.PlannedTonnes);
        Assert.Equal(2, row.Cycles);
        Assert.NotNull(row.AveragePayloadPercent);
    }

    [Fact]
    public void OrdersChronologicallyAcrossMultipleDates()
    {
        var schedules = new[]
        {
            TestData.Schedule("T01", shiftDate: new DateOnly(2026, 9, 2), shiftName: "Day"),
            TestData.Schedule("T01", shiftDate: new DateOnly(2026, 9, 1), shiftName: "Night"),
            TestData.Schedule("T01", shiftDate: new DateOnly(2026, 9, 1), shiftName: "Day"),
        };

        var result = ShiftSeriesCalculator.Calculate([], schedules, AsOfAfterAllShifts);

        Assert.Equal(new DateOnly(2026, 9, 1), result[0].ShiftDate);
        Assert.Equal("Day", result[0].ShiftName);
        Assert.Equal(new DateOnly(2026, 9, 1), result[1].ShiftDate);
        Assert.Equal("Night", result[1].ShiftName);
        Assert.Equal(new DateOnly(2026, 9, 2), result[2].ShiftDate);
    }

    [Fact]
    public void IsComplete_ReflectsWhetherShiftEndIsAfterAsOf()
    {
        var shiftDate = new DateOnly(2026, 9, 1);
        var schedules = new[]
        {
            TestData.Schedule("T01", shiftDate: shiftDate, shiftName: "Day"),
        };

        // Mid-shift: Day shift (06:00-18:00) hasn't ended yet.
        var inProgress = ShiftSeriesCalculator.Calculate([], schedules, new DateTime(2026, 9, 1, 13, 45, 0));
        Assert.False(Assert.Single(inProgress).IsComplete);

        // Exactly at shift end: complete.
        var atEnd = ShiftSeriesCalculator.Calculate([], schedules, new DateTime(2026, 9, 1, 18, 0, 0));
        Assert.True(Assert.Single(atEnd).IsComplete);

        // Well after shift end: complete.
        var afterEnd = ShiftSeriesCalculator.Calculate([], schedules, new DateTime(2026, 9, 2, 0, 0, 0));
        Assert.True(Assert.Single(afterEnd).IsComplete);
    }
}
