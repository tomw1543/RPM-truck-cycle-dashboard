namespace HaulCycle.Api.Kpi;

/// <summary>Average cycle time and the phase split (each phase's share of total cycle minutes).</summary>
public static class CycleTimeCalculator
{
    public sealed record PhaseSplit(
        decimal LoadPercent,
        decimal HaulPercent,
        decimal DumpPercent,
        decimal ReturnPercent,
        decimal QueuePercent);

    public sealed record Result(decimal? AverageCycleMin, PhaseSplit? PhaseSplit, int CycleCount);

    public static Result Calculate(IReadOnlyCollection<CycleRow> cycles)
    {
        if (cycles.Count == 0)
            return new Result(null, null, 0);

        var totalCycleMin = cycles.Sum(c => c.TotalCycleMin);
        var averageCycleMin = totalCycleMin / cycles.Count;

        PhaseSplit? split = null;
        if (totalCycleMin > 0)
        {
            var load = cycles.Sum(c => c.LoadMin);
            var haul = cycles.Sum(c => c.HaulMin);
            var dump = cycles.Sum(c => c.DumpMin);
            var ret = cycles.Sum(c => c.ReturnMin);
            var queue = cycles.Sum(c => c.QueueMin);

            split = new PhaseSplit(
                Math.Round(load / totalCycleMin * 100m, 1),
                Math.Round(haul / totalCycleMin * 100m, 1),
                Math.Round(dump / totalCycleMin * 100m, 1),
                Math.Round(ret / totalCycleMin * 100m, 1),
                Math.Round(queue / totalCycleMin * 100m, 1));
        }

        return new Result(averageCycleMin, split, cycles.Count);
    }
}
