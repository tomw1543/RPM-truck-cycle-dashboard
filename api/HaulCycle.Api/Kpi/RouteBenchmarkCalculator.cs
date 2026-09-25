namespace HaulCycle.Api.Kpi;

/// <summary>Per-route benchmark cycle times: the 25th percentile of total cycle minutes and of
/// each phase, computed over ALL cycles ever recorded for that route (never a requested window -
/// see CONTEXT.md's Benchmark definition). Shared by the routes endpoint and, later, by
/// recoverable-minutes/bottleneck attribution, so it stays general: callers pass every cycle they
/// have and get back a benchmark per route name, independent of whatever window they're about to
/// slice those same cycles by.</summary>
public static class RouteBenchmarkCalculator
{
    public sealed record PhaseBenchmark(
        decimal? QueueMin,
        decimal? LoadMin,
        decimal? HaulMin,
        decimal? DumpMin,
        decimal? ReturnMin);

    public sealed record RouteBenchmark(decimal? TotalCycleMin, PhaseBenchmark Phases);

    /// <summary>One benchmark per route name found in <paramref name="cycles"/>. A route with no
    /// cycles simply has no entry - callers that need every route (including zero-cycle ones)
    /// should treat a missing key the same as all-null.</summary>
    public static IReadOnlyDictionary<string, RouteBenchmark> Calculate(IReadOnlyCollection<CycleRow> cycles)
    {
        var result = new Dictionary<string, RouteBenchmark>();

        foreach (var group in cycles.GroupBy(c => c.RouteName))
        {
            var rows = group.ToList();
            var benchmark = new RouteBenchmark(
                Percentile25(rows.Select(r => r.TotalCycleMin)),
                new PhaseBenchmark(
                    Percentile25(rows.Select(r => r.QueueMin)),
                    Percentile25(rows.Select(r => r.LoadMin)),
                    Percentile25(rows.Select(r => r.HaulMin)),
                    Percentile25(rows.Select(r => r.DumpMin)),
                    Percentile25(rows.Select(r => r.ReturnMin))));
            result[group.Key] = benchmark;
        }

        return result;
    }

    /// <summary>PERCENTILE_CONT(0.25) WITHIN GROUP (ORDER BY x): linear interpolation between the
    /// two nearest ranks over the sorted values, matching SQL Server exactly. Null for an empty
    /// input; a single value is its own 25th percentile.</summary>
    public static decimal? Percentile25(IEnumerable<decimal> values) => Percentile(values, 0.25m);

    /// <summary>PERCENTILE_CONT(p) WITHIN GROUP (ORDER BY x): linear interpolation between the two
    /// nearest ranks over the sorted values, matching SQL Server exactly. Null for an empty input;
    /// a single value is its own percentile at any p.</summary>
    public static decimal? Percentile(IEnumerable<decimal> values, decimal p)
    {
        var sorted = values.OrderBy(v => v).ToList();
        if (sorted.Count == 0)
            return null;
        if (sorted.Count == 1)
            return sorted[0];

        // PERCENTILE_CONT rank (0-based): p * (n - 1).
        var rank = p * (sorted.Count - 1);
        var lowerIndex = (int)Math.Floor(rank);
        var upperIndex = (int)Math.Ceiling(rank);
        if (lowerIndex == upperIndex)
            return sorted[lowerIndex];

        var fraction = rank - lowerIndex;
        return sorted[lowerIndex] + (sorted[upperIndex] - sorted[lowerIndex]) * fraction;
    }
}
