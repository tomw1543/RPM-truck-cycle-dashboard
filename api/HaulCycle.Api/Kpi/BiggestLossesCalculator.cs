namespace HaulCycle.Api.Kpi;

/// <summary>Ranks the ten biggest loss items across three measures that overlap and must never
/// be added together: recoverable minutes by (route, phase), minutes over book by (route,
/// phase), and underload tonnes by truck. Ranked by equivalent tonnes (underload items are
/// already in tonnes; minute items are converted at their own route's rate - see
/// EquivalentTonnesCalculator). A minute item with no route rate (route had no window cycles,
/// which can't actually happen since the minutes themselves come from window cycles, but is
/// handled defensively) is excluded rather than ranked with a fabricated value.</summary>
public static class BiggestLossesCalculator
{
    public const string MeasureRecoverable = "recoverable";
    public const string MeasureOverBook = "overBook";
    public const string MeasureUnderload = "underload";

    public sealed record Item(
        string Measure,
        string Subject,
        string? Phase,
        decimal NativeAmount,
        string NativeUnit,
        decimal EquivalentTonnes);

    public static IReadOnlyList<Item> Calculate(
        RecoverableMinutesCalculator.Result recoverable,
        MinutesOverBookCalculator.Result overBook,
        UnderloadCalculator.Result underload,
        IReadOnlyDictionary<string, decimal> ratesByRoute)
    {
        var items = new List<Item>();

        foreach (var r in recoverable.ByRoutePhase)
            items.AddRange(PhaseItems(MeasureRecoverable, r.RouteName, r.Phases, ratesByRoute));

        foreach (var r in overBook.ByRoute)
            items.AddRange(PhaseItems(MeasureOverBook, r.RouteName, r.Phases, ratesByRoute));

        foreach (var t in underload.ByTruck)
        {
            if (t.UnderloadTonnes > 0m)
                items.Add(new Item(MeasureUnderload, t.TruckName, null, t.UnderloadTonnes, "t", t.UnderloadTonnes));
        }

        return items
            .OrderByDescending(i => i.EquivalentTonnes)
            .Take(10)
            .ToList();
    }

    private static IEnumerable<Item> PhaseItems(
        string measure, string routeName, PhaseAttributionCalculator.PhaseAmounts phases,
        IReadOnlyDictionary<string, decimal> ratesByRoute)
    {
        var byPhase = new (string Name, decimal Minutes)[]
        {
            ("Queue", phases.Queue),
            ("Load", phases.Load),
            ("Haul", phases.Haul),
            ("Dump", phases.Dump),
            ("Return", phases.Return),
            ("Unattributed", phases.Unattributed),
        };

        foreach (var (name, minutes) in byPhase)
        {
            if (minutes <= 0m)
                continue;

            var equivalentTonnes = EquivalentTonnesCalculator.Convert(minutes, routeName, ratesByRoute);
            if (equivalentTonnes is null)
                continue;

            yield return new Item(measure, routeName, name, minutes, "min", equivalentTonnes.Value);
        }
    }
}
