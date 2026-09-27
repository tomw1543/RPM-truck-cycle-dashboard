namespace HaulCycle.Api.Kpi;

/// <summary>Builds the per-truck "why did this move happen" sentence, and a plan-level summary
/// sentence, for one candidate plan (MoreOutput or Leaner) against Original - at read time,
/// from the plan's own stored rows, so a wording change never needs a --optimise rerun (moving
/// this out of the data generator was an owner decision: the generator's old template-built
/// MoveReason stated "spare capacity" and "relieves queueing" even when neither was true for the
/// loader a truck actually moved to).
///
/// Every clause only appears when the numbers behind it actually hold - queue relief and spare
/// capacity are both judged against ORIGINAL's own per-loader stats (the state the owner actually
/// saw that shift), never the candidate's. A truck moved from an under-used loader to a busier
/// one gets neither clause: see the reported bad case (T03, MoreOutput, L1 to L2) in the
/// project's README/CONTEXT.md.</summary>
public static class MoveReasonCalculator
{
    public sealed record Result(IReadOnlyDictionary<string, string> ReasonsByTruck, string Summary);

    private const decimal QueueReliefThresholdMin = 0.5m;
    private const decimal DestinationBindingSlack = 0.05m; // 5% above Original's mean for that destination

    private const string FallbackReason = "Part of a wider rebalance; see the loader table.";

    public static Result Calculate(
        IReadOnlyCollection<OptimisedAssignmentRow> originalAssignments,
        IReadOnlyCollection<OptimisedAssignmentRow> candidateAssignments,
        IReadOnlyCollection<OptimisedLoaderStatRow> originalLoaderStats,
        IReadOnlyCollection<OptimisedLoaderStatRow> candidateLoaderStats,
        OptimisedPlanRow originalPlan,
        OptimisedPlanRow candidatePlan,
        IReadOnlyCollection<RouteReferenceRow> routes)
    {
        var originalByTruck = originalAssignments.ToDictionary(a => a.TruckName);
        var originalLoadersByName = originalLoaderStats.ToDictionary(l => l.LoaderName);
        var candidateLoadersByName = candidateLoaderStats.ToDictionary(l => l.LoaderName);
        var routesByName = routes.ToDictionary(r => r.RouteName);

        var reasons = new Dictionary<string, string>();
        foreach (var candidate in candidateAssignments)
        {
            if (candidate.IsUnavailable) continue;
            if (!originalByTruck.TryGetValue(candidate.TruckName, out var original)) continue;
            if (original.RouteName == candidate.RouteName) continue; // unchanged - no reason

            reasons[candidate.TruckName] = BuildReason(
                original, candidate, originalLoadersByName, candidateLoadersByName, originalPlan, candidatePlan, routesByName);
        }

        var summary = BuildSummary(originalLoaderStats, candidateLoaderStats, candidatePlan.TrucksStoodDownMean);

        return new Result(reasons, summary);
    }

    private static string BuildReason(
        OptimisedAssignmentRow original,
        OptimisedAssignmentRow candidate,
        IReadOnlyDictionary<string, OptimisedLoaderStatRow> originalLoadersByName,
        IReadOnlyDictionary<string, OptimisedLoaderStatRow> candidateLoadersByName,
        OptimisedPlanRow originalPlan,
        OptimisedPlanRow candidatePlan,
        IReadOnlyDictionary<string, RouteReferenceRow> routesByName)
    {
        // Stood down: the truck had a route in Original and has none in the candidate.
        if (candidate.RouteName is null)
            return BuildStoodDownReason(original, originalPlan, candidatePlan, originalLoadersByName, candidateLoadersByName);

        // Original always assigns a route to every available truck (the scheduler never leaves
        // one unassigned), so original.RouteName is null here only for an unavailable truck -
        // already filtered out by the caller. Guard anyway rather than assume.
        if (original.RouteName is null)
            return FallbackReason;

        var fromRoute = routesByName[original.RouteName];
        var toRoute = routesByName[candidate.RouteName];

        if (original.LoaderName == candidate.LoaderName)
            return BuildSameLoaderReason(original, candidate, fromRoute, toRoute, originalPlan, candidatePlan);

        return BuildDifferentLoaderReason(original, candidate, fromRoute, toRoute, originalLoadersByName, originalPlan, candidatePlan);
    }

    private static string BuildStoodDownReason(
        OptimisedAssignmentRow original,
        OptimisedPlanRow originalPlan,
        OptimisedPlanRow candidatePlan,
        IReadOnlyDictionary<string, OptimisedLoaderStatRow> originalLoadersByName,
        IReadOnlyDictionary<string, OptimisedLoaderStatRow> candidateLoadersByName)
    {
        var everyDestinationCovered =
            candidatePlan.CrusherTonnesMean >= originalPlan.CrusherTonnesMean &&
            candidatePlan.RomTonnesMean >= originalPlan.RomTonnesMean &&
            candidatePlan.WasteTonnesMean >= originalPlan.WasteTonnesMean;

        if (!everyDestinationCovered)
            return FallbackReason; // defensive - the optimiser's own floor constraint should make this unreachable

        var reason = "Stood down: the other trucks covered its work, with every destination at or above its original tonnes.";

        var fromLoader = original.LoaderName!;
        if (originalLoadersByName.TryGetValue(fromLoader, out var origStat) &&
            candidateLoadersByName.TryGetValue(fromLoader, out var candStat) &&
            candStat.AvgQueueMin < origStat.AvgQueueMin)
        {
            reason += $" {fromLoader}'s average queue fell from {FormatMin(origStat.AvgQueueMin)} to {FormatMin(candStat.AvgQueueMin)} min.";
        }

        return reason;
    }

    private static string BuildDifferentLoaderReason(
        OptimisedAssignmentRow original,
        OptimisedAssignmentRow candidate,
        RouteReferenceRow fromRoute,
        RouteReferenceRow toRoute,
        IReadOnlyDictionary<string, OptimisedLoaderStatRow> originalLoadersByName,
        OptimisedPlanRow originalPlan,
        OptimisedPlanRow candidatePlan)
    {
        var fromStat = originalLoadersByName[original.LoaderName!];
        var toStat = originalLoadersByName[candidate.LoaderName!];

        var clauses = new List<string>();

        if (fromStat.AvgQueueMin - toStat.AvgQueueMin >= QueueReliefThresholdMin)
            clauses.Add($"Leaves {original.LoaderName} ({FormatMin(fromStat.AvgQueueMin)} min average queue) for {candidate.LoaderName} ({FormatMin(toStat.AvgQueueMin)} min).");

        if (toStat.Utilisation < fromStat.Utilisation)
            clauses.Add($"{candidate.LoaderName} was less busy ({FormatPercent(toStat.Utilisation)} utilised against {original.LoaderName}'s {FormatPercent(fromStat.Utilisation)}).");

        if (toRoute.DistanceKm < fromRoute.DistanceKm)
            clauses.Add($"{toRoute.RouteName} is {FormatKm(fromRoute.DistanceKm - toRoute.DistanceKm)} km shorter than {fromRoute.RouteName}.");

        if (original.DestinationName != candidate.DestinationName && IsDestinationBinding(candidate.DestinationName!, originalPlan, candidatePlan))
            clauses.Add($"Moves to {candidate.DestinationName} to keep it at its original tonnes.");

        return clauses.Count == 0 ? FallbackReason : string.Join(" ", clauses.Take(2));
    }

    private static string BuildSameLoaderReason(
        OptimisedAssignmentRow original,
        OptimisedAssignmentRow candidate,
        RouteReferenceRow fromRoute,
        RouteReferenceRow toRoute,
        OptimisedPlanRow originalPlan,
        OptimisedPlanRow candidatePlan)
    {
        var reason = $"Stays on {candidate.LoaderName}, switches to {candidate.DestinationName}.";

        if (toRoute.DistanceKm < fromRoute.DistanceKm)
            reason += $" {toRoute.RouteName} is {FormatKm(fromRoute.DistanceKm - toRoute.DistanceKm)} km shorter than {fromRoute.RouteName}.";
        else if (IsDestinationBinding(candidate.DestinationName!, originalPlan, candidatePlan))
            reason += $" Moves to {candidate.DestinationName} to keep it at its original tonnes.";

        return reason;
    }

    /// <summary>True when the candidate's mean tonnes to this destination sit at Original's mean
    /// or up to 5% above it - i.e. the move only just covers what Original already delivered
    /// there, so that destination is the binding reason for the move rather than incidental.</summary>
    private static bool IsDestinationBinding(string destinationName, OptimisedPlanRow originalPlan, OptimisedPlanRow candidatePlan)
    {
        var originalMean = DestinationMean(originalPlan, destinationName);
        var candidateMean = DestinationMean(candidatePlan, destinationName);
        if (originalMean <= 0) return false;

        return candidateMean >= originalMean && candidateMean <= originalMean * (1 + DestinationBindingSlack);
    }

    private static decimal DestinationMean(OptimisedPlanRow plan, string destinationName) => destinationName switch
    {
        ActualShiftOutcomeCalculator.CrusherDestination => plan.CrusherTonnesMean,
        ActualShiftOutcomeCalculator.RomPadDestination => plan.RomTonnesMean,
        ActualShiftOutcomeCalculator.WasteDumpDestination => plan.WasteTonnesMean,
        _ => throw new ArgumentException($"Unknown destination '{destinationName}'.", nameof(destinationName)),
    };

    /// <summary>The headline sentence shown above the moves list: which loaders lost/gained
    /// trucks (with their own before/after average queue or utilisation), and how many trucks
    /// were stood down. Only loaders whose truck count actually changed are named. The "moved N
    /// trucks" count is the number that arrived somewhere (sum of gainers) when there is a
    /// gainer, or the number that left (sum of losers) when every loser's trucks were stood down
    /// instead - either way a real headcount, never invented.</summary>
    private static string BuildSummary(
        IReadOnlyCollection<OptimisedLoaderStatRow> originalLoaderStats,
        IReadOnlyCollection<OptimisedLoaderStatRow> candidateLoaderStats,
        decimal trucksStoodDownMean)
    {
        var originalByName = originalLoaderStats.ToDictionary(l => l.LoaderName);
        var candidateByName = candidateLoaderStats.ToDictionary(l => l.LoaderName);
        var standDown = (int)Math.Round(trucksStoodDownMean, MidpointRounding.AwayFromZero);

        var losers = new List<(string LoaderName, int Delta, decimal OrigQueue, decimal CandQueue)>();
        var gainers = new List<(string LoaderName, int Delta, decimal OrigUtil, decimal CandUtil)>();

        foreach (var loaderName in originalByName.Keys.OrderBy(n => n, StringComparer.Ordinal))
        {
            var original = originalByName[loaderName];
            var candidate = candidateByName[loaderName];
            var delta = candidate.Trucks - original.Trucks;
            if (delta < 0) losers.Add((loaderName, -delta, original.AvgQueueMin, candidate.AvgQueueMin));
            else if (delta > 0) gainers.Add((loaderName, delta, original.Utilisation, candidate.Utilisation));
        }

        if (losers.Count == 0 && gainers.Count == 0 && standDown == 0)
            return "Same as the original schedule.";

        var parts = new List<string>();

        // Each loader gets its own count: losses and gains don't balance when trucks are stood
        // down, so one "Moved N trucks" total would misstate one side.
        var clauses = new List<string>();
        if (losers.Count > 0)
            clauses.Add(JoinWithAnd(losers.Select((l, i) =>
                $"{l.LoaderName} lost {TruckCount(l.Delta, i == 0)} (average queue {FormatMin(l.OrigQueue)} to {FormatMin(l.CandQueue)} min)")));
        if (gainers.Count > 0)
            clauses.Add(JoinWithAnd(gainers.Select((g, i) =>
                $"{g.LoaderName} gained {TruckCount(g.Delta, i == 0)} (utilisation {FormatPercent(g.OrigUtil)} to {FormatPercent(g.CandUtil)})")));
        if (clauses.Count > 0)
            parts.Add(string.Join("; ", clauses) + ".");

        if (standDown > 0)
            parts.Add($"Stood down {standDown} truck{(standDown == 1 ? "" : "s")}.");

        return string.Join(" ", parts);
    }

    /// <summary>"2 trucks" the first time in a list, then just "2" ("L2 lost 1 truck and L3 lost 3").</summary>
    private static string TruckCount(int count, bool withNoun) =>
        withNoun ? $"{count} truck{(count == 1 ? "" : "s")}" : count.ToString();

    private static string JoinWithAnd(IEnumerable<string> items)
    {
        var list = items.ToList();
        return list.Count <= 1 ? string.Concat(list) : string.Join(", ", list.Take(list.Count - 1)) + " and " + list[^1];
    }

    private static string FormatMin(decimal minutes) => minutes.ToString("0.0");
    private static string FormatKm(decimal km) => km.ToString("0.0");
    private static string FormatPercent(decimal fraction) => $"{Math.Round(fraction * 100, 0, MidpointRounding.AwayFromZero):0}%";
}
