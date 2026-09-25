namespace HaulCycle.Api.Kpi;

/// <summary>Converts recoverable/over-book minutes into "equivalent tonnes": the tonnes that
/// minute figure would move if it became hauling time, at that route's own rate (route window
/// tonnes / route window cycle minutes, i.e. tonnes per minute observed on the route in the
/// requested window). A route with no window cycles has no rate (null), so minutes on that
/// route convert to null rather than zero or an extrapolated guess. Aggregates that mix routes
/// (by truck/loader/phase) must convert each route's own minutes at that route's own rate
/// before summing - never sum minutes across routes and apply one rate.</summary>
public static class EquivalentTonnesCalculator
{
    /// <summary>Tonnes-per-minute rate per route name, from this window's cycles only.</summary>
    public static IReadOnlyDictionary<string, decimal> RatesByRoute(IReadOnlyCollection<CycleRow> windowCycles)
    {
        var rates = new Dictionary<string, decimal>();
        foreach (var group in windowCycles.GroupBy(c => c.RouteName))
        {
            var minutes = group.Sum(c => c.TotalCycleMin);
            if (minutes > 0m)
                rates[group.Key] = group.Sum(c => c.PayloadTonnes) / minutes;
        }
        return rates;
    }

    public static decimal? Convert(decimal minutes, string routeName, IReadOnlyDictionary<string, decimal> ratesByRoute) =>
        ratesByRoute.TryGetValue(routeName, out var rate) ? minutes * rate : null;
}
