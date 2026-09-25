namespace HaulCycle.Api.Kpi;

/// <summary>Recoverable minutes (see CONTEXT.md): per cycle, gap = max(0, TotalCycleMin -
/// route's all-time P25 total), attributed across phases against the route's all-time P25 per
/// phase (RouteBenchmarkCalculator - never the requested window). Aggregates: total, by phase
/// (+ Unattributed), by route, by truck, by loader, and by (route, phase). All minute
/// arithmetic is exact decimal, unrounded - callers round only when formatting a value for
/// display, so summed components stay internally consistent.
///
/// Equivalent tonnes (see EquivalentTonnesCalculator) are converted per cycle, at that cycle's
/// own route rate, and summed - never by summing minutes across routes first and applying one
/// rate - so ByTruck/ByLoader/ByPhase equivalent-tonnes totals stay meaningful even though a
/// truck or loader can span several routes with different rates.</summary>
public static class RecoverableMinutesCalculator
{
    public sealed record NamedAmount(string Name, decimal Minutes, decimal? EquivalentTonnes);

    public sealed record RoutePhaseMinutes(string RouteName, decimal TotalMin, decimal? EquivalentTonnes, PhaseAttributionCalculator.PhaseAmounts Phases);

    public sealed record Result(
        decimal TotalMin,
        decimal TotalEquivalentTonnes,
        PhaseAttributionCalculator.PhaseAmounts ByPhase,
        IReadOnlyList<NamedAmount> ByRoute,
        IReadOnlyList<NamedAmount> ByTruck,
        IReadOnlyList<NamedAmount> ByLoader,
        IReadOnlyList<RoutePhaseMinutes> ByRoutePhase);

    public static Result Calculate(
        IReadOnlyCollection<CycleRow> windowCycles,
        IReadOnlyDictionary<string, RouteBenchmarkCalculator.RouteBenchmark> benchmarks,
        IReadOnlyDictionary<string, decimal> ratesByRoute)
    {
        decimal totalMin = 0m;
        decimal totalEquivalentTonnes = 0m;
        var byPhase = PhaseAttributionCalculator.Zero;
        var byRouteMin = new Dictionary<string, decimal>();
        var byRouteTonnes = new Dictionary<string, decimal>();
        var byTruckMin = new Dictionary<string, decimal>();
        var byTruckTonnes = new Dictionary<string, decimal>();
        var byLoaderMin = new Dictionary<string, decimal>();
        var byLoaderTonnes = new Dictionary<string, decimal>();
        var byRoutePhase = new Dictionary<string, PhaseAttributionCalculator.PhaseAmounts>();

        foreach (var c in windowCycles)
        {
            if (!benchmarks.TryGetValue(c.RouteName, out var b) || b.TotalCycleMin is null)
                continue;

            var gap = Math.Max(0m, c.TotalCycleMin - b.TotalCycleMin.Value);
            if (gap <= 0m)
                continue;

            var amounts = PhaseAttributionCalculator.Attribute(
                gap, c.QueueMin, c.LoadMin, c.HaulMin, c.DumpMin, c.ReturnMin,
                b.Phases.QueueMin, b.Phases.LoadMin, b.Phases.HaulMin, b.Phases.DumpMin, b.Phases.ReturnMin);

            var equivalentTonnes = EquivalentTonnesCalculator.Convert(gap, c.RouteName, ratesByRoute) ?? 0m;

            totalMin += gap;
            totalEquivalentTonnes += equivalentTonnes;
            byPhase = Add(byPhase, amounts);

            byRouteMin[c.RouteName] = byRouteMin.GetValueOrDefault(c.RouteName) + gap;
            byRouteTonnes[c.RouteName] = byRouteTonnes.GetValueOrDefault(c.RouteName) + equivalentTonnes;

            byTruckMin[c.TruckName] = byTruckMin.GetValueOrDefault(c.TruckName) + gap;
            byTruckTonnes[c.TruckName] = byTruckTonnes.GetValueOrDefault(c.TruckName) + equivalentTonnes;

            byLoaderMin[c.LoaderName] = byLoaderMin.GetValueOrDefault(c.LoaderName) + gap;
            byLoaderTonnes[c.LoaderName] = byLoaderTonnes.GetValueOrDefault(c.LoaderName) + equivalentTonnes;

            byRoutePhase[c.RouteName] = Add(byRoutePhase.GetValueOrDefault(c.RouteName, PhaseAttributionCalculator.Zero), amounts);
        }

        var routePhaseRows = byRoutePhase
            .Select(kv => new RoutePhaseMinutes(
                kv.Key,
                byRouteMin[kv.Key],
                EquivalentTonnesCalculator.Convert(byRouteMin[kv.Key], kv.Key, ratesByRoute),
                kv.Value))
            .OrderByDescending(r => r.TotalMin)
            .ToList();

        return new Result(
            totalMin,
            totalEquivalentTonnes,
            byPhase,
            ToSorted(byRouteMin, byRouteTonnes),
            ToSorted(byTruckMin, byTruckTonnes),
            ToSorted(byLoaderMin, byLoaderTonnes),
            routePhaseRows);
    }

    private static PhaseAttributionCalculator.PhaseAmounts Add(PhaseAttributionCalculator.PhaseAmounts a, PhaseAttributionCalculator.PhaseAmounts b) =>
        new(a.Queue + b.Queue, a.Load + b.Load, a.Haul + b.Haul, a.Dump + b.Dump, a.Return + b.Return, a.Unattributed + b.Unattributed);

    private static IReadOnlyList<NamedAmount> ToSorted(Dictionary<string, decimal> minutes, Dictionary<string, decimal> tonnes) =>
        minutes
            .Select(kv => new NamedAmount(kv.Key, kv.Value, tonnes.GetValueOrDefault(kv.Key)))
            .OrderByDescending(n => n.Minutes)
            .ToList();
}
