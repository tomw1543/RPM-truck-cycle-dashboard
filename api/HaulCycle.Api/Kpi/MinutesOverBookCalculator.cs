namespace HaulCycle.Api.Kpi;

/// <summary>Minutes over book (see CONTEXT.md): per cycle, gap = max(0, TotalCycleMin - route's
/// BookCycleMin), attributed across phases against the route's per-phase book minutes. Catches
/// slowness that affects every cycle on a route (which the P25 benchmark absorbs and so misses -
/// see RecoverableMinutesCalculator). Never added to recoverable minutes; the two overlap.
/// Aggregates: total, by phase (+ Unattributed), and by route (each with its own phase split and
/// equivalent tonnes at that route's own rate).</summary>
public static class MinutesOverBookCalculator
{
    public sealed record RoutePhaseMinutes(string RouteName, decimal TotalMin, decimal? EquivalentTonnes, PhaseAttributionCalculator.PhaseAmounts Phases);

    public sealed record Result(
        decimal TotalMin,
        decimal TotalEquivalentTonnes,
        PhaseAttributionCalculator.PhaseAmounts ByPhase,
        IReadOnlyList<RoutePhaseMinutes> ByRoute);

    public static Result Calculate(
        IReadOnlyCollection<CycleRow> windowCycles,
        IReadOnlyDictionary<string, RouteBookRow> routesByName,
        IReadOnlyDictionary<string, decimal> ratesByRoute)
    {
        decimal totalMin = 0m;
        decimal totalEquivalentTonnes = 0m;
        var byPhase = PhaseAttributionCalculator.Zero;
        var byRoutePhase = new Dictionary<string, PhaseAttributionCalculator.PhaseAmounts>();
        var byRouteTotal = new Dictionary<string, decimal>();

        foreach (var c in windowCycles)
        {
            if (!routesByName.TryGetValue(c.RouteName, out var route))
                continue;

            var gap = Math.Max(0m, c.TotalCycleMin - route.BookCycleMin);
            if (gap <= 0m)
                continue;

            var amounts = PhaseAttributionCalculator.Attribute(
                gap, c.QueueMin, c.LoadMin, c.HaulMin, c.DumpMin, c.ReturnMin,
                route.BookQueueMin, route.BookLoadMin, route.BookHaulMin, route.BookDumpMin, route.BookReturnMin);

            var equivalentTonnes = EquivalentTonnesCalculator.Convert(gap, c.RouteName, ratesByRoute) ?? 0m;

            totalMin += gap;
            totalEquivalentTonnes += equivalentTonnes;
            byPhase = Add(byPhase, amounts);

            byRoutePhase[c.RouteName] = Add(byRoutePhase.GetValueOrDefault(c.RouteName, PhaseAttributionCalculator.Zero), amounts);
            byRouteTotal[c.RouteName] = byRouteTotal.GetValueOrDefault(c.RouteName) + gap;
        }

        var routeRows = byRoutePhase
            .Select(kv => new RoutePhaseMinutes(
                kv.Key,
                byRouteTotal[kv.Key],
                EquivalentTonnesCalculator.Convert(byRouteTotal[kv.Key], kv.Key, ratesByRoute),
                kv.Value))
            .OrderByDescending(r => r.TotalMin)
            .ToList();

        return new Result(totalMin, totalEquivalentTonnes, byPhase, routeRows);
    }

    private static PhaseAttributionCalculator.PhaseAmounts Add(PhaseAttributionCalculator.PhaseAmounts a, PhaseAttributionCalculator.PhaseAmounts b) =>
        new(a.Queue + b.Queue, a.Load + b.Load, a.Haul + b.Haul, a.Dump + b.Dump, a.Return + b.Return, a.Unattributed + b.Unattributed);
}
