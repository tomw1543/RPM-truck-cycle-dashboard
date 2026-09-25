namespace HaulCycle.Api.Kpi;

/// <summary>Underload tonnes (see CONTEXT.md): the baseline fill is the median (50th percentile)
/// of PayloadPercentOfCapacity over ALL cycles ever recorded, not the requested window - the same
/// all-data principle RouteBenchmarkCalculator uses for its P25 route benchmarks. Per cycle,
/// underload tonnes = max(0, baselineFill/100 * CapacityTonnes - PayloadTonnes). Measures tonnes
/// lost to trucks running below the fleet's typical load - a measure cycle-time recoverable
/// minutes miss, since a light load also loads faster. Aggregated by truck, with the truck's own
/// average payload percent alongside for context.</summary>
public static class UnderloadCalculator
{
    public sealed record TruckUnderload(string TruckName, int Cycles, decimal UnderloadTonnes, decimal? AveragePayloadPercent);

    public sealed record Result(decimal TotalTonnes, IReadOnlyList<TruckUnderload> ByTruck, decimal BaselinePayloadPercent);

    public static Result Calculate(IReadOnlyCollection<CycleRow> windowCycles, IReadOnlyCollection<CycleRow> allCycles)
    {
        var baselinePayloadPercent = RouteBenchmarkCalculator.Percentile(
            allCycles.Select(c => c.PayloadPercentOfCapacity), 0.5m) ?? 0m;

        var byTruck = windowCycles
            .GroupBy(c => c.TruckName)
            .Select(g =>
            {
                var cycles = g.ToList();
                var underloadTonnes = cycles.Sum(c => Math.Max(0m, baselinePayloadPercent / 100m * c.CapacityTonnes - c.PayloadTonnes));
                var avgPayloadPercent = cycles.Count > 0 ? cycles.Average(c => c.PayloadPercentOfCapacity) : (decimal?)null;
                return new TruckUnderload(g.Key, cycles.Count, underloadTonnes, avgPayloadPercent);
            })
            .OrderByDescending(t => t.UnderloadTonnes)
            .ToList();

        var total = byTruck.Sum(t => t.UnderloadTonnes);
        return new Result(total, byTruck, baselinePayloadPercent);
    }
}
