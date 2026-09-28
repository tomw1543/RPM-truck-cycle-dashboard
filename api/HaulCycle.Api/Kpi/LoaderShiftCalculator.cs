namespace HaulCycle.Api.Kpi;

/// <summary>/api/loaders: one row per loader per shift in the window, on the same definitions
/// OptimisedLoaderStats uses (see OptimiserEngine.ScoreOneSeed in data-generator/Optimiser.cs)
/// so this page and the optimiser's before/after loader table agree:
///   Trucks = trucks the Schedules assigned to that loader for the shift.
///   AvgQueueMin, LoadingMin = from that shift's actual cycles at the loader (LoadingMin is the
///     sum of LoadMin, not an average).
///   StoppedMin = RateFactor 0.00 LoaderDelays minutes clipped to the shift window.
///   Utilisation = LoadingMin / (720 - StoppedMin).
///   MatchFactor = Trucks x average LoadMin / average TotalCycleMin at that loader (0 with no
///     cycles - matches MatchFactorCalculator's shape, but per loader-shift instead of fleet-wide).</summary>
public static class LoaderShiftCalculator
{
    public sealed record LoaderShiftResult(
        DateOnly ShiftDate,
        string ShiftName,
        bool IsComplete,
        string LoaderName,
        int Trucks,
        decimal AvgQueueMin,
        decimal LoadingMin,
        decimal StoppedMin,
        decimal Utilisation,
        decimal MatchFactor);

    public sealed record LoaderSummary(
        string LoaderName,
        decimal AvgUtilisation,
        decimal AvgQueueMin,
        decimal AvgMatchFactor);

    public sealed record Result(IReadOnlyList<LoaderShiftResult> Shifts, IReadOnlyList<LoaderSummary> Summary);

    public static Result Calculate(
        IReadOnlyCollection<CycleRow> cycles,
        IReadOnlyCollection<ScheduleRow> schedules,
        IReadOnlyCollection<LoaderDelayRow> loaderDelays,
        IReadOnlyCollection<string> loaderNames,
        DateTime asOf)
    {
        var cyclesByShiftLoader = cycles
            .GroupBy(c => (c.ShiftDate, c.ShiftName, c.LoaderName))
            .ToDictionary(g => g.Key, g => g.ToList());

        var trucksByShiftLoader = schedules
            .Where(s => s.LoaderName is not null)
            .GroupBy(s => (s.ShiftDate, s.ShiftName, LoaderName: s.LoaderName!))
            .ToDictionary(g => g.Key, g => g.Select(s => s.TruckName).Distinct().Count());

        var shiftKeys = schedules
            .Select(s => (s.ShiftDate, s.ShiftName))
            .Concat(cycles.Select(c => (c.ShiftDate, c.ShiftName)))
            .Distinct()
            .OrderByDescending(k => k.ShiftDate)
            .ThenByDescending(k => k.ShiftName == "Night")
            .ToList();

        var results = new List<LoaderShiftResult>();
        foreach (var key in shiftKeys)
        {
            var shiftStart = ShiftWindow.Start(key.ShiftDate, key.ShiftName);
            var shiftEnd = ShiftWindow.End(key.ShiftDate, key.ShiftName);
            var isComplete = shiftEnd <= asOf;

            foreach (var loaderName in loaderNames)
            {
                var loaderCycles = cyclesByShiftLoader.TryGetValue((key.ShiftDate, key.ShiftName, loaderName), out var lc) ? lc : [];
                var trucks = trucksByShiftLoader.TryGetValue((key.ShiftDate, key.ShiftName, loaderName), out var tr) ? tr : 0;

                var stoppedMin = loaderDelays
                    .Where(d => d.LoaderName == loaderName && d.RateFactor == 0.00m)
                    .Sum(d => OverlapMinutes(d.StartTime, d.EndTime, shiftStart, shiftEnd));

                var avgQueueMin = loaderCycles.Count > 0 ? loaderCycles.Average(c => c.QueueMin) : 0m;
                var loadingMin = loaderCycles.Sum(c => c.LoadMin);

                var denom = 720m - stoppedMin;
                var utilisation = denom > 0m ? loadingMin / denom : 0m;

                decimal matchFactor = 0m;
                if (loaderCycles.Count > 0)
                {
                    var avgLoadMin = loaderCycles.Average(c => c.LoadMin);
                    var avgTotalCycleMin = loaderCycles.Average(c => c.TotalCycleMin);
                    if (avgTotalCycleMin > 0m)
                        matchFactor = trucks * avgLoadMin / avgTotalCycleMin;
                }

                if (trucks == 0 && loaderCycles.Count == 0)
                    continue;

                results.Add(new LoaderShiftResult(
                    key.ShiftDate, key.ShiftName, isComplete, loaderName,
                    trucks, avgQueueMin, loadingMin, stoppedMin, utilisation, matchFactor));
            }
        }

        var summary = results
            .Where(r => r.IsComplete)
            .GroupBy(r => r.LoaderName)
            .Select(g => new LoaderSummary(
                g.Key,
                g.Average(r => r.Utilisation),
                g.Average(r => r.AvgQueueMin),
                g.Average(r => r.MatchFactor)))
            .OrderBy(s => s.LoaderName, StringComparer.Ordinal)
            .ToList();

        return new Result(results, summary);
    }

    private static decimal OverlapMinutes(DateTime aStart, DateTime aEnd, DateTime bStart, DateTime bEnd)
    {
        var start = aStart > bStart ? aStart : bStart;
        var end = aEnd < bEnd ? aEnd : bEnd;
        var span = end - start;
        return span > TimeSpan.Zero ? (decimal)span.TotalMinutes : 0m;
    }
}
