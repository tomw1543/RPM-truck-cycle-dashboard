namespace HaulCycle.Api.Kpi;

/// <summary>Average payload percent (0-100, like CycleRow.PayloadPercentOfCapacity) and cycles
/// per operating hour. Neither is covered by the other calculators: TonnesPerHourCalculator
/// rates tonnes, not cycle count, and nothing else looks at payload share directly.</summary>
public static class PayloadRateCalculator
{
    public sealed record Result(decimal? AveragePayloadPercent, decimal? CyclesPerOperatingHour);

    public static Result Calculate(IReadOnlyCollection<CycleRow> cycles)
    {
        if (cycles.Count == 0)
            return new Result(null, null);

        var averagePayloadPercent = cycles.Average(c => c.PayloadPercentOfCapacity);

        var operatingHours = cycles.Sum(c => c.TotalCycleMin) / 60m;
        var cyclesPerOperatingHour = operatingHours > 0 ? cycles.Count / operatingHours : (decimal?)null;

        return new Result(averagePayloadPercent, cyclesPerOperatingHour);
    }
}
