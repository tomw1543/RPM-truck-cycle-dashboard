namespace HaulCycle.Api.Kpi;

/// <summary>Shared phase-attribution rule (see CONTEXT.md's Phase attribution) used by both
/// recoverable minutes (reference = route P25 benchmark) and minutes over book (reference =
/// route book time). Given a cycle's gap G (its total minus some reference total, floored at
/// zero) and each phase's own excess over its own reference (floored at zero), the gap is split
/// across phases in proportion to those excesses. When every phase excess is zero (possible,
/// since percentiles/book totals don't add across phases) the whole gap goes to Unattributed.
/// Shares + Unattributed always sum to exactly G: everything here is exact decimal arithmetic,
/// with no rounding until a caller formats a value for display.</summary>
public static class PhaseAttributionCalculator
{
    public sealed record PhaseAmounts(decimal Queue, decimal Load, decimal Haul, decimal Dump, decimal Return, decimal Unattributed)
    {
        public decimal Total => Queue + Load + Haul + Dump + Return + Unattributed;
    }

    public static readonly PhaseAmounts Zero = new(0m, 0m, 0m, 0m, 0m, 0m);

    public static PhaseAmounts Attribute(
        decimal gap,
        decimal queueMin, decimal loadMin, decimal haulMin, decimal dumpMin, decimal returnMin,
        decimal? refQueueMin, decimal? refLoadMin, decimal? refHaulMin, decimal? refDumpMin, decimal? refReturnMin)
    {
        if (gap <= 0)
            return Zero;

        var eq = Excess(queueMin, refQueueMin);
        var el = Excess(loadMin, refLoadMin);
        var eh = Excess(haulMin, refHaulMin);
        var ed = Excess(dumpMin, refDumpMin);
        var er = Excess(returnMin, refReturnMin);
        var sum = eq + el + eh + ed + er;

        if (sum <= 0)
            return Zero with { Unattributed = gap };

        // Four shares come straight from the proportion; the fifth (Return) is gap minus the
        // other four, not its own proportional division - decimal division truncates at ~28
        // significant digits, so five independent divisions can miss summing back to gap by an
        // ULP. Taking one share as the remainder makes Total == gap exactly, not just to
        // decimal's precision limit, with no visible effect on the (2-dp-rounded) displayed value.
        var queue = gap * eq / sum;
        var load = gap * el / sum;
        var haul = gap * eh / sum;
        var dump = gap * ed / sum;
        var ret = gap - queue - load - haul - dump;

        return new PhaseAmounts(queue, load, haul, dump, ret, 0m);
    }

    private static decimal Excess(decimal actual, decimal? reference) =>
        reference.HasValue ? Math.Max(0m, actual - reference.Value) : 0m;
}
