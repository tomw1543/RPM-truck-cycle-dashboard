using HaulCycle.Api.Kpi;

namespace HaulCycle.Api.Tests;

public class TonnesPerHourCalculatorTests
{
    private static readonly DateTime Start = new(2026, 9, 1, 0, 0, 0);

    [Fact]
    public void EmptyWindow_ReturnsNullRatesNotDivideByZero()
    {
        var result = TonnesPerHourCalculator.Calculate([], Start, Start);

        Assert.Null(result.TonnesPerOperatingHour);
        Assert.Null(result.TonnesPerCalendarHour);
        Assert.Equal(0m, result.TotalTonnes);
    }

    [Fact]
    public void NoCycles_OperatingHourRateIsNullEvenWithACalendarWindow()
    {
        var to = Start.AddHours(10);
        var result = TonnesPerHourCalculator.Calculate([], Start, to);

        Assert.Null(result.TonnesPerOperatingHour);
        Assert.Equal(0m, result.TonnesPerCalendarHour); // 0 tonnes / 10h = 0, a real rate, not undefined
    }

    [Fact]
    public void ComputesBothBases()
    {
        var to = Start.AddHours(24);
        var cycles = new[]
        {
            TestData.Cycle(start: Start, payloadTonnes: 1000m, totalCycleMin: 300m),
            TestData.Cycle(start: Start.AddHours(1), payloadTonnes: 1000m, totalCycleMin: 300m),
        };

        var result = TonnesPerHourCalculator.Calculate(cycles, Start, to);

        Assert.Equal(2000m, result.TotalTonnes);
        Assert.Equal(10m, result.OperatingHours); // 600 minutes
        Assert.Equal(24m, result.CalendarHours);
        Assert.Equal(200m, result.TonnesPerOperatingHour); // 2000 / 10
        Assert.Equal(2000m / 24m, result.TonnesPerCalendarHour);
    }
}
