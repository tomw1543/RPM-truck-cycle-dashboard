namespace HaulCycle.Api.Kpi;

/// <summary>/api/schedule/compliance's shortfall attribution: for every truck-shift with a plan
/// (a Schedules row with PlannedTonnes), decomposes gap = PlannedTonnes - ActualTonnes exactly
/// (to decimal rounding) into signed tonne buckets. See the README for the plan basis and
/// bucket definitions; the short version:
///
///   gap = payloadShort + r x (queueLoaderDelay + queueOverTrucking + haulOverTarget +
///         otherOverTarget + unplannedDowntime + residual)
///
/// where r = CapacityTonnes / route TargetCycleMin (tonnes per minute) and every minute bucket is
/// itself in minutes before the r conversion. Buckets are signed throughout (Q2): a negative
/// bucket means the truck-shift gained tonnes there (e.g. T07 loading faster than target), never
/// floored to zero. Unavailable trucks (no plan) are excluded entirely - callers report their
/// count/reasons from the existing Schedules-derived data instead.
///
/// The overlap rule (queue split) is a documented simplification: attributing queue-over-target
/// minutes to "loader delay" only for the minutes that literally overlap a LoaderDelays window
/// slightly under-counts knock-on queue that builds up just after a delay ends but before the
/// backlog clears - that spillover lands in overTrucking instead.</summary>
public static class ShortfallAttributionCalculator
{
    public sealed record Buckets(
        decimal PayloadShort,
        decimal UnplannedDowntime,
        decimal QueueLoaderDelay,
        decimal QueueOverTrucking,
        decimal HaulOverTarget,
        decimal OtherOverTarget,
        decimal Residual)
    {
        public decimal Total => PayloadShort + UnplannedDowntime + QueueLoaderDelay + QueueOverTrucking
            + HaulOverTarget + OtherOverTarget + Residual;
    }

    public static readonly Buckets Zero = new(0m, 0m, 0m, 0m, 0m, 0m, 0m);

    public sealed record TruckResult(string TruckName, string? RouteName, string? LoaderName, decimal Gap, Buckets Buckets);

    public sealed record ShiftResult(
        DateOnly ShiftDate,
        string ShiftName,
        bool IsComplete,
        decimal Gap,
        Buckets Buckets,
        IReadOnlyList<TruckResult> Trucks);

    public sealed record WindowResult(decimal Gap, Buckets Buckets, int ShiftCount);

    public sealed record Result(WindowResult Window, IReadOnlyList<ShiftResult> Shifts);

    public static Result Calculate(
        IReadOnlyCollection<CycleRow> cycles,
        IReadOnlyCollection<ScheduleRow> schedules,
        IReadOnlyCollection<DelayRow> delays,
        IReadOnlyCollection<LoaderDelayRow> loaderDelays,
        IReadOnlyDictionary<string, RouteTargetRow> routesByName,
        IReadOnlyDictionary<string, decimal> capacityByTruck,
        DateTime asOf)
    {
        var cyclesByTruckShift = cycles
            .GroupBy(c => (c.ShiftDate, c.ShiftName, c.TruckName))
            .ToDictionary(g => g.Key, g => g.ToList());

        var delaysByTruck = delays
            .Where(d => !d.IsPlanned)
            .GroupBy(d => d.TruckName)
            .ToDictionary(g => g.Key, g => g.ToList());

        var loaderDelaysByLoader = loaderDelays
            .GroupBy(d => d.LoaderName)
            .ToDictionary(g => g.Key, g => g.ToList());

        var plannedSchedules = schedules
            .Where(s => s.PlannedTonnes.HasValue && s.RouteName is not null)
            .ToList();

        var shiftKeys = plannedSchedules
            .Select(s => (s.ShiftDate, s.ShiftName))
            .Distinct()
            .OrderByDescending(k => k.ShiftDate)
            .ThenByDescending(k => k.ShiftName == "Night");

        var shiftResults = new List<ShiftResult>();
        foreach (var key in shiftKeys)
        {
            var shiftStart = ShiftWindow.Start(key.ShiftDate, key.ShiftName);
            var shiftEnd = ShiftWindow.End(key.ShiftDate, key.ShiftName);
            var isComplete = shiftEnd <= asOf;

            var truckResults = new List<TruckResult>();
            foreach (var schedule in plannedSchedules.Where(s => s.ShiftDate == key.ShiftDate && s.ShiftName == key.ShiftName))
            {
                if (!routesByName.TryGetValue(schedule.RouteName!, out var scheduledTarget))
                    continue;
                if (!capacityByTruck.TryGetValue(schedule.TruckName, out var capacity))
                    continue;
                if (scheduledTarget.TargetCycleMin <= 0m)
                    continue;

                var truckCycles = cyclesByTruckShift.TryGetValue((key.ShiftDate, key.ShiftName, schedule.TruckName), out var tc) ? tc : [];
                var truckDelays = delaysByTruck.TryGetValue(schedule.TruckName, out var td) ? td : [];

                var rate = capacity / scheduledTarget.TargetCycleMin;
                var plannedCycles = schedule.PlannedCycles ?? 0m;
                var availableMin = plannedCycles * scheduledTarget.TargetCycleMin;

                var payloadShort = truckCycles.Sum(c => c.CapacityTonnes - c.PayloadTonnes);

                decimal queueLoaderDelayMin = 0m, queueOverTruckingMin = 0m, haulOverTargetMin = 0m, otherOverTargetMin = 0m;
                decimal sumTotalCycleMin = 0m;

                foreach (var c in truckCycles)
                {
                    sumTotalCycleMin += c.TotalCycleMin;
                    if (!routesByName.TryGetValue(c.RouteName, out var target))
                        continue;

                    var queueOverTarget = c.QueueMin - target.TargetQueueMin;
                    var haulOverTarget = c.HaulMin - target.TargetHaulMin;
                    var otherOverTarget = (c.LoadMin - target.TargetLoadMin) + (c.DumpMin - target.TargetDumpMin) + (c.ReturnMin - target.TargetReturnMin);

                    var queueStart = c.StartTime;
                    var queueEnd = c.StartTime.AddMinutes((double)c.QueueMin);
                    var loaderDelaysAtLoader = loaderDelaysByLoader.TryGetValue(c.LoaderName, out var ld) ? ld : [];
                    decimal overlapMin = 0m;
                    foreach (var d in loaderDelaysAtLoader)
                        overlapMin += OverlapMinutes(queueStart, queueEnd, d.StartTime, d.EndTime);

                    var cap = Math.Max(0m, queueOverTarget);
                    var loaderDelayQueue = Math.Clamp(overlapMin, 0m, cap);
                    var overTruckingQueue = queueOverTarget - loaderDelayQueue;

                    queueLoaderDelayMin += loaderDelayQueue;
                    queueOverTruckingMin += overTruckingQueue;
                    haulOverTargetMin += haulOverTarget;
                    otherOverTargetMin += otherOverTarget;
                }

                var unplannedDowntimeMin = truckDelays.Sum(d => OverlapMinutes(d.StartTime, d.EndTime, shiftStart, shiftEnd));

                var residualMin = availableMin - sumTotalCycleMin - unplannedDowntimeMin;

                var buckets = new Buckets(
                    payloadShort,
                    rate * unplannedDowntimeMin,
                    rate * queueLoaderDelayMin,
                    rate * queueOverTruckingMin,
                    rate * haulOverTargetMin,
                    rate * otherOverTargetMin,
                    rate * residualMin);

                var actualTonnes = truckCycles.Sum(c => c.PayloadTonnes);
                var gap = (schedule.PlannedTonnes ?? 0m) - actualTonnes;

                truckResults.Add(new TruckResult(schedule.TruckName, schedule.RouteName, schedule.LoaderName, gap, buckets));
            }

            var shiftGap = truckResults.Sum(t => t.Gap);
            var shiftBuckets = truckResults.Aggregate(Zero, (acc, t) => Add(acc, t.Buckets));

            shiftResults.Add(new ShiftResult(key.ShiftDate, key.ShiftName, isComplete, shiftGap, shiftBuckets, truckResults));
        }

        var completeShifts = shiftResults.Where(s => s.IsComplete).ToList();
        var windowGap = completeShifts.Sum(s => s.Gap);
        var windowBuckets = completeShifts.Aggregate(Zero, (acc, s) => Add(acc, s.Buckets));
        var window = new WindowResult(windowGap, windowBuckets, completeShifts.Count);

        return new Result(window, shiftResults);
    }

    private static Buckets Add(Buckets a, Buckets b) => new(
        a.PayloadShort + b.PayloadShort,
        a.UnplannedDowntime + b.UnplannedDowntime,
        a.QueueLoaderDelay + b.QueueLoaderDelay,
        a.QueueOverTrucking + b.QueueOverTrucking,
        a.HaulOverTarget + b.HaulOverTarget,
        a.OtherOverTarget + b.OtherOverTarget,
        a.Residual + b.Residual);

    private static decimal OverlapMinutes(DateTime aStart, DateTime aEnd, DateTime bStart, DateTime bEnd)
    {
        var start = aStart > bStart ? aStart : bStart;
        var end = aEnd < bEnd ? aEnd : bEnd;
        var span = end - start;
        return span > TimeSpan.Zero ? (decimal)span.TotalMinutes : 0m;
    }
}
