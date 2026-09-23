namespace HaulCycle.Api.Kpi;

/// <summary>
/// Match factor = (trucks x average load time) / (loaders x average truck cycle time).
/// Below 1, loaders wait for trucks. Above 1, trucks queue for loaders.
/// Truck/loader counts are fleet-wide reference counts, not "trucks active in the window",
/// so a quiet window doesn't inflate the factor.
/// </summary>
public static class MatchFactorCalculator
{
    public static decimal? Calculate(IReadOnlyCollection<CycleRow> cycles, int truckCount, int loaderCount)
    {
        if (cycles.Count == 0 || truckCount <= 0 || loaderCount <= 0)
            return null;

        var averageLoadMin = cycles.Average(c => c.LoadMin);
        var averageCycleMin = cycles.Average(c => c.TotalCycleMin);

        if (averageCycleMin <= 0)
            return null;

        return (truckCount * averageLoadMin) / (loaderCount * averageCycleMin);
    }
}
