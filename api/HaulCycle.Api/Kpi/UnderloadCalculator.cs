namespace HaulCycle.Api.Kpi;

/// <summary>Underload tonnes (see CONTEXT.md): per cycle, max(0, CapacityTonnes - PayloadTonnes).
/// Measures tonnes lost to part-filled trucks - a measure cycle-time recoverable minutes miss,
/// since a light load also loads faster. Aggregated by truck, with the truck's own average
/// payload percent alongside for context.</summary>
public static class UnderloadCalculator
{
    public sealed record TruckUnderload(string TruckName, int Cycles, decimal UnderloadTonnes, decimal? AveragePayloadPercent);

    public sealed record Result(decimal TotalTonnes, IReadOnlyList<TruckUnderload> ByTruck);

    public static Result Calculate(IReadOnlyCollection<CycleRow> windowCycles)
    {
        var byTruck = windowCycles
            .GroupBy(c => c.TruckName)
            .Select(g =>
            {
                var cycles = g.ToList();
                var underloadTonnes = cycles.Sum(c => Math.Max(0m, c.CapacityTonnes - c.PayloadTonnes));
                var avgPayloadPercent = cycles.Count > 0 ? cycles.Average(c => c.PayloadPercentOfCapacity) : (decimal?)null;
                return new TruckUnderload(g.Key, cycles.Count, underloadTonnes, avgPayloadPercent);
            })
            .OrderByDescending(t => t.UnderloadTonnes)
            .ToList();

        var total = byTruck.Sum(t => t.UnderloadTonnes);
        return new Result(total, byTruck);
    }
}
