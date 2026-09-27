namespace HaulCycle.Api.Kpi;

/// <summary>/api/optimiser/summary: the whole-window headline for each candidate plan type
/// (MoreOutput, Leaner) against Original, plus a per-shift list for the shift picker.
///
/// Range convention: a plan type's whole-period tonnes-gained range is the SUM, across every
/// shift that has both an Original and that plan type's result, of that shift's own
/// (candidate.Min - original.Max) for the conservative end and (candidate.Max - original.Min)
/// for the optimistic end - not a re-derived range over the summed means. This is the same
/// convention used to hand-verify the optimiser's whole-period numbers directly against SQL (see
/// CONTEXT.md's Optimiser glossary entry); it is a genuinely conservative/optimistic bound
/// (worst-case min paired with the other side's best-case max) rather than a statistically
/// precise range for the sum, which would need the per-shift outcomes' joint distribution.
///
/// QueueHoursSaved/FuelLitresSaved/TruckHoursSaved are Original minus the candidate, summed
/// per shift, so a positive number always means "the candidate used less" - the sign a value page
/// wants to show as a saving.</summary>
public static class OptimiserSummaryCalculator
{
    public sealed record PlanGain(
        decimal TonnesGainedMean, decimal TonnesGainedMin, decimal TonnesGainedMax,
        decimal QueueHoursSaved, decimal FuelLitresSaved, decimal TruckHoursSaved, decimal TrucksStoodDown);

    public sealed record ShiftHeadline(
        DateOnly ShiftDate, string ShiftName, bool HasResults,
        decimal? MoreOutputTonnesGainedMean, decimal? LeanerTruckHoursSavedMean);

    public sealed record Result(
        int ShiftsOptimised, int ShiftsNotOptimised,
        PlanGain MoreOutput, PlanGain Leaner,
        IReadOnlyList<ShiftHeadline> Shifts);

    /// <param name="dataStart">When the data begins. A shift that starts before it is only partly
    /// covered, so --optimise never optimises it; it is left out of the list rather than shown as
    /// "not optimised yet", since rerunning --optimise would never change that.</param>
    public static Result Calculate(IReadOnlyCollection<ScheduleRow> schedules, IReadOnlyCollection<OptimisedPlanRow> plans, DateTime asOf, DateTime? dataStart = null)
    {
        var completeShiftKeys = schedules
            .Select(s => (s.ShiftDate, s.ShiftName))
            .Distinct()
            .Where(k => ShiftWindow.End(k.ShiftDate, k.ShiftName) <= asOf)
            .Where(k => dataStart is null || ShiftWindow.End(k.ShiftDate, k.ShiftName).AddHours(-12) >= dataStart.Value)
            .ToHashSet();

        var plansByShift = plans
            .GroupBy(p => (p.ShiftDate, p.ShiftName))
            .ToDictionary(g => g.Key, g => g.ToList());

        var optimisedShiftKeys = plansByShift.Keys.ToHashSet();
        var shiftsOptimised = optimisedShiftKeys.Count;
        var shiftsNotOptimised = Math.Max(0, completeShiftKeys.Count - shiftsOptimised);

        var moreOutput = AggregatePlanType(plansByShift, "MoreOutput");
        var leaner = AggregatePlanType(plansByShift, "Leaner");

        var shiftHeadlines = completeShiftKeys
            .Concat(optimisedShiftKeys)
            .Distinct()
            .OrderByDescending(k => k.ShiftDate)
            .ThenByDescending(k => k.ShiftName == "Night")
            .Select(k => BuildHeadline(k, plansByShift))
            .ToList();

        return new Result(shiftsOptimised, shiftsNotOptimised, moreOutput, leaner, shiftHeadlines);
    }

    private static ShiftHeadline BuildHeadline(
        (DateOnly ShiftDate, string ShiftName) key,
        IReadOnlyDictionary<(DateOnly, string), List<OptimisedPlanRow>> plansByShift)
    {
        var hasResults = plansByShift.TryGetValue(key, out var rows);
        var original = hasResults ? rows!.FirstOrDefault(r => r.PlanType == "Original") : null;
        var moreOutput = hasResults ? rows!.FirstOrDefault(r => r.PlanType == "MoreOutput") : null;
        var leaner = hasResults ? rows!.FirstOrDefault(r => r.PlanType == "Leaner") : null;

        decimal? moreOutputGain = (original is not null && moreOutput is not null)
            ? moreOutput.TotalTonnesMean - original.TotalTonnesMean
            : null;
        decimal? leanerSaved = (original is not null && leaner is not null)
            ? original.TruckHoursMean - leaner.TruckHoursMean
            : null;

        return new ShiftHeadline(key.ShiftDate, key.ShiftName, hasResults, moreOutputGain, leanerSaved);
    }

    private static PlanGain AggregatePlanType(
        IReadOnlyDictionary<(DateOnly, string), List<OptimisedPlanRow>> plansByShift, string planType)
    {
        decimal tonnesMean = 0, tonnesMin = 0, tonnesMax = 0;
        decimal queueSaved = 0, fuelSaved = 0, truckHoursSaved = 0, stoodDown = 0;

        foreach (var rows in plansByShift.Values)
        {
            var original = rows.FirstOrDefault(r => r.PlanType == "Original");
            var candidate = rows.FirstOrDefault(r => r.PlanType == planType);
            if (original is null || candidate is null) continue;

            tonnesMean += candidate.TotalTonnesMean - original.TotalTonnesMean;
            tonnesMin += candidate.TotalTonnesMin - original.TotalTonnesMax;
            tonnesMax += candidate.TotalTonnesMax - original.TotalTonnesMin;
            queueSaved += original.QueueHoursMean - candidate.QueueHoursMean;
            fuelSaved += original.FuelLitresMean - candidate.FuelLitresMean;
            truckHoursSaved += original.TruckHoursMean - candidate.TruckHoursMean;
            stoodDown += candidate.TrucksStoodDownMean;
        }

        return new PlanGain(tonnesMean, tonnesMin, tonnesMax, queueSaved, fuelSaved, truckHoursSaved, stoodDown);
    }
}
