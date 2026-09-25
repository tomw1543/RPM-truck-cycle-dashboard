namespace HaulCycle.Api.Kpi;

/// <summary>Queue hotspots: loader queue benchmark is the 25th percentile of QueueMin per
/// loader, computed over ALL cycles ever recorded (same convention as RouteBenchmarkCalculator -
/// never the requested window). Per cycle, excess = max(0, QueueMin - that loader's P25). Two
/// views over the window's cycles: the top 10 (loader, date-hour) buckets by total excess
/// minutes, and a full loader x hour-of-day (0-23) profile of summed excess minutes - both
/// bucketed by StartTime's hour in mine time (DATEPART(HOUR, StartTime), same as the DB would
/// compute it), since that's how the data is stored.</summary>
public static class QueueHotspotsCalculator
{
    public sealed record HotspotRow(string LoaderName, DateTime DateHour, decimal ExcessMin, int Cycles, decimal AverageQueueMin);

    public sealed record ProfileCell(string LoaderName, int HourOfDay, decimal ExcessMin, int Cycles);

    public sealed record Result(
        IReadOnlyDictionary<string, decimal?> BenchmarkByLoader,
        IReadOnlyList<HotspotRow> Top,
        IReadOnlyList<ProfileCell> Profile);

    /// <summary>25th-percentile QueueMin per loader, over every cycle passed in - callers must
    /// pass ALL cycles (never a window subset) for this to be a stable benchmark.</summary>
    public static IReadOnlyDictionary<string, decimal?> BenchmarkByLoader(IReadOnlyCollection<CycleRow> allCycles) =>
        allCycles
            .GroupBy(c => c.LoaderName)
            .ToDictionary(g => g.Key, g => RouteBenchmarkCalculator.Percentile25(g.Select(c => c.QueueMin)));

    public static Result Calculate(IReadOnlyCollection<CycleRow> allCyclesForBenchmark, IReadOnlyCollection<CycleRow> windowCycles)
    {
        var benchmark = BenchmarkByLoader(allCyclesForBenchmark);

        var hotspotCycles = new Dictionary<(string Loader, DateTime Hour), List<CycleRow>>();
        var hotspotExcess = new Dictionary<(string Loader, DateTime Hour), decimal>();
        var profileExcess = new Dictionary<(string Loader, int Hour), decimal>();
        var profileCount = new Dictionary<(string Loader, int Hour), int>();

        foreach (var c in windowCycles)
        {
            if (!benchmark.TryGetValue(c.LoaderName, out var p25) || p25 is null)
                continue;

            var excess = Math.Max(0m, c.QueueMin - p25.Value);

            var hourBucket = new DateTime(c.StartTime.Year, c.StartTime.Month, c.StartTime.Day, c.StartTime.Hour, 0, 0);
            var hotspotKey = (c.LoaderName, hourBucket);
            if (!hotspotCycles.TryGetValue(hotspotKey, out var list))
            {
                list = [];
                hotspotCycles[hotspotKey] = list;
            }
            list.Add(c);
            hotspotExcess[hotspotKey] = hotspotExcess.GetValueOrDefault(hotspotKey) + excess;

            var profileKey = (c.LoaderName, c.StartTime.Hour);
            profileExcess[profileKey] = profileExcess.GetValueOrDefault(profileKey) + excess;
            profileCount[profileKey] = profileCount.GetValueOrDefault(profileKey) + 1;
        }

        var top = hotspotCycles
            .Select(kv => new HotspotRow(
                kv.Key.Loader,
                kv.Key.Hour,
                hotspotExcess[kv.Key],
                kv.Value.Count,
                kv.Value.Average(c => c.QueueMin)))
            .Where(r => r.ExcessMin > 0m)
            .OrderByDescending(r => r.ExcessMin)
            .Take(10)
            .ToList();

        var profile = profileExcess
            .Select(kv => new ProfileCell(kv.Key.Loader, kv.Key.Hour, kv.Value, profileCount[kv.Key]))
            .OrderBy(c => c.LoaderName).ThenBy(c => c.HourOfDay)
            .ToList();

        return new Result(benchmark, top, profile);
    }
}
