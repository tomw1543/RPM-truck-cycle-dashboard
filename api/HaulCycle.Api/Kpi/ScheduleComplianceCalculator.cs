namespace HaulCycle.Api.Kpi;

/// <summary>/api/schedule/compliance: one row per shift (fleet-wide planned vs actual tonnes and
/// cycles, unavailable trucks, per-truck breakdown), on the same shift-grained basis
/// (GetCyclesForShiftWindowAsync + GetSchedulesAsync) that PlanVsActualCalculator and
/// ShiftSeriesCalculator use.
///
/// Baseline is the tonnes-weighted percent of plan across complete shifts in the window. It is
/// computed by delegating to PlanVsActualCalculator on the same cycles/schedules, so it is
/// identical - not just close - to the fleet summary's planVsActual.percentOfPlan for the same
/// window.
///
/// A shift's or truck's BelowTypical flag only applies to complete shifts: an in-progress shift's
/// percent of plan is naturally low (partial actuals against full planned tonnes) and says
/// nothing about performance. It also requires a baseline to compare against, which needs at
/// least one complete shift with a plan.</summary>
public static class ScheduleComplianceCalculator
{
    /// <summary>A complete shift counts as below typical when its percent of plan falls more than
    /// this many points under the window's baseline.</summary>
    public const decimal ShiftBelowTypicalMargin = 0.05m;

    /// <summary>A truck-shift counts as below typical when its percent of plan falls more than
    /// this many points under the window's baseline.</summary>
    public const decimal TruckBelowTypicalMargin = 0.10m;

    public sealed record ReasonCount(string Reason, int Count);

    public sealed record TruckShiftResult(
        string TruckName,
        string? RouteName,
        string? LoaderName,
        string? UnavailableReason,
        decimal? PlannedTonnes,
        decimal ActualTonnes,
        decimal? PlannedCycles,
        int ActualCycles,
        decimal? PercentOfPlan,
        decimal? AveragePayloadPercent,
        bool BelowTypical);

    public sealed record ShiftResult(
        DateOnly ShiftDate,
        string ShiftName,
        bool IsComplete,
        decimal? PlannedTonnes,
        decimal ActualTonnes,
        decimal? PlannedCycles,
        int ActualCycles,
        decimal? PercentOfPlan,
        bool BelowTypical,
        int UnavailableCount,
        IReadOnlyList<ReasonCount> UnavailableReasons,
        IReadOnlyList<TruckShiftResult> Trucks);

    public sealed record ShiftSummary(DateOnly ShiftDate, string ShiftName, decimal PercentOfPlan);

    public sealed record SummaryResult(
        decimal? PlannedTonnes,
        decimal ActualTonnes,
        decimal? PlannedCycles,
        int ActualCycles,
        decimal? PercentOfPlan,
        decimal? Baseline,
        int ShiftCount,
        ShiftSummary? Best,
        ShiftSummary? Worst);

    public sealed record Result(SummaryResult Summary, IReadOnlyList<ShiftResult> Shifts);

    public static Result Calculate(IReadOnlyCollection<CycleRow> cycles, IReadOnlyCollection<ScheduleRow> schedules, DateTime asOf)
    {
        // Delegating to PlanVsActualCalculator (rather than re-deriving the same ratio) is what
        // guarantees the baseline equals /api/fleet/summary's planVsActual.percentOfPlan for the
        // same inputs.
        var planVsActual = PlanVsActualCalculator.Calculate(cycles, schedules, asOf);
        var baseline = planVsActual.PercentOfPlan;

        var cyclesByShift = cycles
            .GroupBy(c => (c.ShiftDate, c.ShiftName))
            .ToDictionary(g => g.Key, g => g.ToList());

        var schedulesByShift = schedules
            .GroupBy(s => (s.ShiftDate, s.ShiftName))
            .ToDictionary(g => g.Key, g => g.ToList());

        // Newest first: within the same ShiftDate, Night (18:00-06:00) ends after Day
        // (06:00-18:00), so Night sorts before Day.
        var shiftKeys = schedulesByShift.Keys
            .Concat(cyclesByShift.Keys)
            .Distinct()
            .OrderByDescending(k => k.ShiftDate)
            .ThenByDescending(k => k.ShiftName == "Night");

        var shiftResults = new List<ShiftResult>();
        foreach (var key in shiftKeys)
        {
            var shiftCycles = cyclesByShift.TryGetValue(key, out var cl) ? cl : [];
            var shiftSchedules = schedulesByShift.TryGetValue(key, out var sl) ? sl : [];
            var isComplete = ShiftWindow.End(key.ShiftDate, key.ShiftName) <= asOf;

            var trucks = CalculateTrucks(shiftCycles, shiftSchedules, isComplete, baseline);

            var shiftActualTonnes = shiftCycles.Sum(c => c.PayloadTonnes);
            var shiftActualCycles = shiftCycles.Count;

            var hasAnyPlannedTonnes = shiftSchedules.Any(s => s.PlannedTonnes.HasValue);
            decimal? shiftPlannedTonnes = hasAnyPlannedTonnes ? shiftSchedules.Sum(s => s.PlannedTonnes ?? 0m) : null;

            var hasAnyPlannedCycles = shiftSchedules.Any(s => s.PlannedCycles.HasValue);
            decimal? shiftPlannedCycles = hasAnyPlannedCycles ? shiftSchedules.Sum(s => s.PlannedCycles ?? 0m) : null;

            var shiftPercentOfPlan = shiftPlannedTonnes is > 0 ? shiftActualTonnes / shiftPlannedTonnes : null;

            var shiftBelowTypical = isComplete && shiftPercentOfPlan.HasValue && baseline.HasValue
                && shiftPercentOfPlan.Value < baseline.Value - ShiftBelowTypicalMargin;

            var unavailable = shiftSchedules.Where(s => s.UnavailableReason is not null).ToList();
            var unavailableReasons = unavailable
                .GroupBy(s => s.UnavailableReason!)
                .Select(g => new ReasonCount(g.Key, g.Count()))
                .OrderByDescending(r => r.Count)
                .ThenBy(r => r.Reason, StringComparer.Ordinal)
                .ToList();

            shiftResults.Add(new ShiftResult(
                key.ShiftDate,
                key.ShiftName,
                isComplete,
                shiftPlannedTonnes,
                shiftActualTonnes,
                shiftPlannedCycles,
                shiftActualCycles,
                shiftPercentOfPlan,
                shiftBelowTypical,
                unavailable.Count,
                unavailableReasons,
                trucks));
        }

        var summary = CalculateSummary(shiftResults, planVsActual, baseline);

        return new Result(summary, shiftResults);
    }

    private static IReadOnlyList<TruckShiftResult> CalculateTrucks(
        IReadOnlyList<CycleRow> shiftCycles,
        IReadOnlyList<ScheduleRow> shiftSchedules,
        bool isComplete,
        decimal? baseline)
    {
        var cyclesByTruck = shiftCycles.GroupBy(c => c.TruckName).ToDictionary(g => g.Key, g => g.ToList());
        var schedulesByTruck = shiftSchedules.ToDictionary(s => s.TruckName);

        var truckNames = schedulesByTruck.Keys
            .Concat(cyclesByTruck.Keys)
            .Distinct()
            .OrderBy(n => n, StringComparer.Ordinal);

        var results = new List<TruckShiftResult>();
        foreach (var truckName in truckNames)
        {
            schedulesByTruck.TryGetValue(truckName, out var schedule);
            var truckCycles = cyclesByTruck.TryGetValue(truckName, out var tc) ? tc : [];

            var actualTonnes = truckCycles.Sum(c => c.PayloadTonnes);
            var actualCycles = truckCycles.Count;
            decimal? averagePayloadPercent = truckCycles.Count > 0 ? truckCycles.Average(c => c.PayloadPercentOfCapacity) : null;

            var plannedTonnes = schedule?.PlannedTonnes;
            var plannedCycles = schedule?.PlannedCycles;
            var percentOfPlan = plannedTonnes is > 0 ? actualTonnes / plannedTonnes : null;

            var belowTypical = isComplete && percentOfPlan.HasValue && baseline.HasValue
                && percentOfPlan.Value < baseline.Value - TruckBelowTypicalMargin;

            results.Add(new TruckShiftResult(
                truckName,
                schedule?.RouteName,
                schedule?.LoaderName,
                schedule?.UnavailableReason,
                plannedTonnes,
                actualTonnes,
                plannedCycles,
                actualCycles,
                percentOfPlan,
                averagePayloadPercent,
                belowTypical));
        }

        return results;
    }

    private static SummaryResult CalculateSummary(
        IReadOnlyList<ShiftResult> shiftResults,
        PlanVsActualCalculator.Result planVsActual,
        decimal? baseline)
    {
        var completeShifts = shiftResults.Where(s => s.IsComplete).ToList();

        var hasAnyPlannedCycles = completeShifts.Any(s => s.PlannedCycles.HasValue);
        decimal? totalPlannedCycles = hasAnyPlannedCycles ? completeShifts.Sum(s => s.PlannedCycles ?? 0m) : null;
        var totalActualCycles = completeShifts.Sum(s => s.ActualCycles);

        ShiftSummary? best = null;
        ShiftSummary? worst = null;
        var rankable = completeShifts.Where(s => s.PercentOfPlan.HasValue).ToList();
        if (rankable.Count > 0)
        {
            var bestShift = rankable.OrderByDescending(s => s.PercentOfPlan!.Value).First();
            var worstShift = rankable.OrderBy(s => s.PercentOfPlan!.Value).First();
            best = new ShiftSummary(bestShift.ShiftDate, bestShift.ShiftName, bestShift.PercentOfPlan!.Value);
            worst = new ShiftSummary(worstShift.ShiftDate, worstShift.ShiftName, worstShift.PercentOfPlan!.Value);
        }

        return new SummaryResult(
            planVsActual.PlannedTonnes,
            planVsActual.ActualTonnes,
            totalPlannedCycles,
            totalActualCycles,
            planVsActual.PercentOfPlan,
            baseline,
            completeShifts.Count,
            best,
            worst);
    }
}
