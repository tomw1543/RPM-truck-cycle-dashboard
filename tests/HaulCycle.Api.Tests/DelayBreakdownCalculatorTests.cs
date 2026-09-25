using HaulCycle.Api.Kpi;

namespace HaulCycle.Api.Tests;

public class DelayBreakdownCalculatorTests
{
    private static readonly DateTime Start = new(2026, 9, 1, 0, 0, 0);

    [Fact]
    public void NoDelays_ReturnsEmpty()
    {
        var result = DelayBreakdownCalculator.Calculate([], Start, Start.AddDays(1), shift: null);

        Assert.Empty(result);
    }

    [Fact]
    public void GroupsByReasonAndPlannedFlag_SortedByMinutesDescending()
    {
        var to = Start.AddHours(12);
        var delays = new[]
        {
            TestData.Delay("T01", Start, Start.AddMinutes(30), reason: "Refuel", isPlanned: true),
            TestData.Delay("T01", Start.AddHours(2), Start.AddHours(2).AddMinutes(30), reason: "Refuel", isPlanned: true),
            TestData.Delay("T01", Start.AddHours(4), Start.AddHours(6), reason: "Breakdown", isPlanned: false),
        };

        var result = DelayBreakdownCalculator.Calculate(delays, Start, to, shift: null);

        Assert.Equal(2, result.Count);
        Assert.Equal("Breakdown", result[0].Reason); // 120 min > 60 min, sorted first
        Assert.False(result[0].IsPlanned);
        Assert.Equal(1, result[0].Count);
        Assert.Equal(120m, result[0].Minutes);

        Assert.Equal("Refuel", result[1].Reason);
        Assert.True(result[1].IsPlanned);
        Assert.Equal(2, result[1].Count);
        Assert.Equal(60m, result[1].Minutes);
    }

    [Fact]
    public void DelayStraddlingWindowEdge_OnlyCountsTheOverlappingPortion()
    {
        // Delay runs from 1h before the window to 1h into it - only the 1h inside [from, to)
        // should be counted.
        var from = Start;
        var to = Start.AddHours(6);
        var delays = new[] { TestData.Delay("T01", Start.AddHours(-1), Start.AddHours(1), reason: "Maintenance", isPlanned: true) };

        var result = DelayBreakdownCalculator.Calculate(delays, from, to, shift: null);

        var row = Assert.Single(result);
        Assert.Equal(60m, row.Minutes);
        Assert.Equal(1, row.Count);
    }

    [Fact]
    public void DelayEntirelyOutsideWindow_IsDropped()
    {
        var from = Start;
        var to = Start.AddHours(6);
        var delays = new[] { TestData.Delay("T01", Start.AddHours(-3), Start.AddHours(-1), reason: "Handover") };

        var result = DelayBreakdownCalculator.Calculate(delays, from, to, shift: null);

        Assert.Empty(result);
    }

    [Fact]
    public void ShiftFilter_ClipsMinutesToShiftHours()
    {
        // Delay spans 05:00-19:00 (crossing both boundaries); with shift=Day only the
        // 06:00-18:00 portion (12h = 720 min) should count.
        var from = Start;
        var to = Start.AddDays(1);
        var delays = new[] { TestData.Delay("T01", Start.AddHours(5), Start.AddHours(19), reason: "Breakdown") };

        var result = DelayBreakdownCalculator.Calculate(delays, from, to, shift: "Day");

        var row = Assert.Single(result);
        Assert.Equal(720m, row.Minutes);
    }
}
