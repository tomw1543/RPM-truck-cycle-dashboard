// HaulCycle Insights - shift optimiser (Phase 2 slice 2).
//
// Local search over the previous night's already-generated Schedules: for every complete shift,
// build a candidate assignment better than the shift as run (either "MoreOutput", maximising
// mean total tonnes, or "Leaner", minimising truck-hours then fuel then queue hours), judged by
// SimulationEngine.ReplayShift, never guessed. Every plan's outcome is the mean over five
// independent, per-truck-deterministic replays (see SimulationEngine.SeededRandom); ranges are
// the min/max of that same five-outcome set. No DB access happens inside the search itself -
// OptimiserRun.RunAsync reads everything a shift needs up front, hands it to pure in-memory
// methods, and writes results back in a single transaction at the end.

/// <summary>One shift's identity plus its Day/Night boundaries in mine time.</summary>
readonly record struct ShiftKey(DateOnly ShiftDate, string ShiftName)
{
    public DateTime Start => ShiftName == "Day"
        ? ShiftDate.ToDateTime(new TimeOnly(6, 0))
        : ShiftDate.ToDateTime(new TimeOnly(18, 0));

    public DateTime End => Start.AddHours(12);

    public override string ToString() => $"{ShiftDate:yyyy-MM-dd} {ShiftName}";
}

/// <summary>The nine scalar outcomes tracked for one replay (one seed, one plan). Cycles and
/// TrucksStoodDown are kept as double so Mean/Min/Max share a type; they are whole numbers at
/// heart. TruckHours and TrucksStoodDown never vary across seeds for the same plan - they are
/// determined entirely by the plan's assignment, not by any random draw - so a plan's Mean/Min/Max
/// for those two fields are always equal; that is expected, not a bug.</summary>
readonly record struct ReplayScalars(
    double CrusherTonnes, double RomTonnes, double WasteTonnes, double TotalTonnes,
    double Cycles, double QueueHours, double FuelLitres, double TruckHours, double TrucksStoodDown);

/// <summary>One loader's stats for one replay. Trucks is plan-fixed (not random); the rest are
/// computed from that replay's cycles and the shift's fixed loader delays.</summary>
readonly record struct LoaderOutcome(int LoaderId, int Trucks, double AvgQueueMin, double LoadingMin, double Utilisation, double MatchFactor);

/// <summary>A plan's outcome aggregated over OptimiserEngine.SeedCount replays: Mean/Min/Max are
/// per-field extrema across the seed set, not one coherent replay (a plan's Min TotalTonnes and
/// Min QueueHours can come from different seeds - this is the documented, simplest way to report
/// a range, and matches "mean, range" in the spec rather than "the worst single replay").</summary>
readonly record struct AggregatedScalars(ReplayScalars Mean, ReplayScalars Min, ReplayScalars Max, IReadOnlyList<ReplayScalars> PerSeed);

/// <summary>One truck's assignment inside a candidate or final plan. No MoveReason field here -
/// the API builds that sentence at read time (MoveReasonCalculator), not the generator, so a
/// wording change never needs a --optimise rerun.</summary>
class TruckAssignment
{
    public required int TruckId { get; init; }
    public int? RouteId { get; set; }
    public required bool IsUnavailable { get; init; } // fixed for the whole search - never touched
    public bool IsStoodDown => !IsUnavailable && RouteId == null;
}

/// <summary>A finished plan, ready to score or to write to the database.</summary>
class OptimisedPlan
{
    public required string PlanType { get; init; } // "Original" | "MoreOutput" | "Leaner"
    public required Dictionary<int, TruckAssignment> Assignments { get; init; }
    public required AggregatedScalars Scalars { get; init; }
    public required IReadOnlyList<LoaderOutcome> LoaderStats { get; init; }
}

/// <summary>Fixed, per-shift inputs the whole search shares: nothing in here changes between
/// plan variants or between replay seeds - only which route (if any) each truck runs changes.</summary>
class ShiftContext
{
    public required ShiftKey Shift { get; init; }
    public required Fleet Fleet { get; init; }
    public required Simulator Sim { get; init; }
    public required IReadOnlyDictionary<int, List<PendingDelay>> KnownDelaysByTruckId { get; init; }
    public required LoaderPlanner BaseLoaderPlanner { get; init; } // already planned through Shift.End; never mutated directly, only Cloned
    public required IReadOnlySet<int> AvailableTruckIds { get; init; } // trucks whose ORIGINAL route was non-null
}

static class OptimiserEngine
{
    public const int SeedCount = 5;

    // Fixed replay seeds for scoring, deliberately independent of the data-generation --seed:
    // the optimiser's own judgement of a plan must not change just because the history behind it
    // was generated with a different --seed. Any 5 distinct ints would do; these are arbitrary.
    private static readonly int[] ReplaySeeds = { 8001, 8002, 8003, 8004, 8005 };

    private const double ConstraintEpsilon = 1e-6;

    // ------------------------------------------------------------------
    // Scoring
    // ------------------------------------------------------------------

    /// <summary>Runs one replay (one seed) of `routeByTruckId` and returns its scalar outcome
    /// plus per-loader stats. Pure function of its inputs: no DB access, no shared mutable state
    /// beyond the LoaderPlanner clone created inside (which is thrown away after the call).</summary>
    public static (ReplayScalars Scalars, List<LoaderOutcome> LoaderStats) ScoreOneSeed(
        ShiftContext ctx, IReadOnlyDictionary<int, int?> routeByTruckId, int seed)
    {
        var loaderClone = ctx.BaseLoaderPlanner.Clone(SimulationEngine.SeededRandom(seed, ctx.Shift.Start, SimulationEngine.LoaderEntityId));
        var events = SimulationEngine.ReplayShift(
            ctx.Fleet, ctx.Sim, ctx.Shift.Start, ctx.Shift.End,
            routeByTruckId, ctx.KnownDelaysByTruckId, loaderClone, seed);
        var cycles = events.OfType<CycleRow>().ToList();

        double crusher = 0, rom = 0, waste = 0;
        foreach (var c in cycles)
        {
            var route = ctx.Fleet.RouteById(c.RouteId);
            if (route.DestinationId == Fleet.CrusherDestinationId) crusher += c.PayloadTonnes;
            else if (route.DestinationId == Fleet.RomPadDestinationId) rom += c.PayloadTonnes;
            else waste += c.PayloadTonnes;
        }

        var queueHours = cycles.Sum(c => c.QueueMin) / 60.0;
        var fuel = cycles.Sum(c => c.FuelLitres);

        // Truck-hours: 12 (shift length) times the number of AVAILABLE trucks this plan does NOT
        // stand down. Deliberately not reduced for known delays (refuel, crib, maintenance)
        // inside the shift - those are already reflected in the scheduler's planned-cycles
        // figure elsewhere; this metric answers "how many truck-shifts did the plan roster",
        // a simple headcount, not a mix of two different things ("minus nothing clever").
        var stoodDown = ctx.AvailableTruckIds.Count(id => routeByTruckId.TryGetValue(id, out var r) && r == null);
        var rostered = ctx.AvailableTruckIds.Count - stoodDown;
        var truckHours = rostered * 12.0;

        var loaderStats = new List<LoaderOutcome>();
        foreach (var loader in ctx.Fleet.Loaders)
        {
            var trucksAtLoader = ctx.AvailableTruckIds.Count(id =>
                routeByTruckId.TryGetValue(id, out var r) && r != null && ctx.Fleet.RouteById(r.Value).LoaderId == loader.Id);
            var cyclesAtLoader = cycles.Where(c => c.LoaderId == loader.Id).ToList();

            var stoppedMin = ctx.BaseLoaderPlanner.AllDelays
                .Where(d => d.LoaderId == loader.Id && d.RateFactor == 0.00)
                .Sum(d => OverlapMinutes(d.StartTime, d.EndTime, ctx.Shift.Start, ctx.Shift.End));

            var loadingMin = cyclesAtLoader.Sum(c => c.LoadMin);
            var avgQueue = cyclesAtLoader.Count > 0 ? cyclesAtLoader.Average(c => c.QueueMin) : 0.0;
            var avgLoadTime = cyclesAtLoader.Count > 0 ? cyclesAtLoader.Average(c => c.LoadMin) : 0.0;
            var avgTruckCycleTime = cyclesAtLoader.Count > 0 ? cyclesAtLoader.Average(Simulator.TotalMin) : 0.0;

            var denom = 720.0 - stoppedMin;
            var utilisation = denom > 0 ? loadingMin / denom : 0.0;
            var matchFactor = (trucksAtLoader > 0 && avgTruckCycleTime > 0)
                ? trucksAtLoader * avgLoadTime / avgTruckCycleTime
                : 0.0;

            loaderStats.Add(new LoaderOutcome(loader.Id, trucksAtLoader, avgQueue, loadingMin, utilisation, matchFactor));
        }

        var scalars = new ReplayScalars(
            Math.Round(crusher, 1), Math.Round(rom, 1), Math.Round(waste, 1), Math.Round(crusher + rom + waste, 1),
            cycles.Count, Math.Round(queueHours, 3), Math.Round(fuel, 1), truckHours, stoodDown);

        return (scalars, loaderStats);
    }

    /// <summary>Scores a plan as the mean over SeedCount replays, keeping every per-seed outcome
    /// (for Min/Max) and averaging loader stats across the same seeds.</summary>
    public static (AggregatedScalars Scalars, List<LoaderOutcome> LoaderStats) Score(
        ShiftContext ctx, IReadOnlyDictionary<int, int?> routeByTruckId)
    {
        var perSeed = new List<ReplayScalars>();
        List<LoaderOutcome>[] loaderStatsPerSeed = new List<LoaderOutcome>[ReplaySeeds.Length];

        for (var i = 0; i < ReplaySeeds.Length; i++)
        {
            var (scalars, loaders) = ScoreOneSeed(ctx, routeByTruckId, ReplaySeeds[i]);
            perSeed.Add(scalars);
            loaderStatsPerSeed[i] = loaders;
        }

        var mean = Average(perSeed);
        var min = Extreme(perSeed, Math.Min);
        var max = Extreme(perSeed, Math.Max);

        var loaderStatsMean = ctx.Fleet.Loaders.Select(loader =>
        {
            var forLoader = loaderStatsPerSeed.Select(l => l.First(x => x.LoaderId == loader.Id)).ToList();
            return new LoaderOutcome(
                loader.Id,
                forLoader[0].Trucks, // plan-fixed, identical across seeds
                forLoader.Average(x => x.AvgQueueMin),
                forLoader.Average(x => x.LoadingMin),
                forLoader.Average(x => x.Utilisation),
                forLoader.Average(x => x.MatchFactor));
        }).ToList();

        return (new AggregatedScalars(mean, min, max, perSeed), loaderStatsMean);
    }

    private static ReplayScalars Average(List<ReplayScalars> xs) => new(
        xs.Average(x => x.CrusherTonnes), xs.Average(x => x.RomTonnes), xs.Average(x => x.WasteTonnes), xs.Average(x => x.TotalTonnes),
        xs.Average(x => x.Cycles), xs.Average(x => x.QueueHours), xs.Average(x => x.FuelLitres),
        xs.Average(x => x.TruckHours), xs.Average(x => x.TrucksStoodDown));

    private static ReplayScalars Extreme(List<ReplayScalars> xs, Func<double, double, double> pick) => new(
        xs.Select(x => x.CrusherTonnes).Aggregate(pick), xs.Select(x => x.RomTonnes).Aggregate(pick),
        xs.Select(x => x.WasteTonnes).Aggregate(pick), xs.Select(x => x.TotalTonnes).Aggregate(pick),
        xs.Select(x => x.Cycles).Aggregate(pick), xs.Select(x => x.QueueHours).Aggregate(pick),
        xs.Select(x => x.FuelLitres).Aggregate(pick), xs.Select(x => x.TruckHours).Aggregate(pick),
        xs.Select(x => x.TrucksStoodDown).Aggregate(pick));

    private static double OverlapMinutes(DateTime aStart, DateTime aEnd, DateTime bStart, DateTime bEnd)
    {
        var start = aStart > bStart ? aStart : bStart;
        var end = aEnd < bEnd ? aEnd : bEnd;
        return end > start ? (end - start).TotalMinutes : 0;
    }

    // ------------------------------------------------------------------
    // Local search
    // ------------------------------------------------------------------

    /// <summary>Every single-truck move (to one of the 9 routes, or stood down) and every
    /// pairwise swap of two available trucks' current routes, from `current`. Unavailable
    /// trucks are never in `ctx.AvailableTruckIds`, so they never appear here.</summary>
    private static IEnumerable<Dictionary<int, int?>> Neighbours(ShiftContext ctx, Dictionary<int, int?> current)
    {
        var available = ctx.AvailableTruckIds.ToList();

        foreach (var truckId in available)
        {
            var currentRoute = current[truckId];
            foreach (var route in ctx.Fleet.Routes)
            {
                if (route.Id == currentRoute) continue;
                var candidate = new Dictionary<int, int?>(current) { [truckId] = route.Id };
                yield return candidate;
            }
            if (currentRoute != null)
            {
                var candidate = new Dictionary<int, int?>(current) { [truckId] = null };
                yield return candidate;
            }
        }

        for (var i = 0; i < available.Count; i++)
        for (var j = i + 1; j < available.Count; j++)
        {
            var a = available[i];
            var b = available[j];
            if (current[a] == current[b]) continue; // no-op swap
            var candidate = new Dictionary<int, int?>(current) { [a] = current[b], [b] = current[a] };
            yield return candidate;
        }
    }

    private static bool MeetsDestinationConstraint(AggregatedScalars candidate, AggregatedScalars original) =>
        candidate.Mean.CrusherTonnes >= original.Mean.CrusherTonnes - ConstraintEpsilon &&
        candidate.Mean.RomTonnes >= original.Mean.RomTonnes - ConstraintEpsilon &&
        candidate.Mean.WasteTonnes >= original.Mean.WasteTonnes - ConstraintEpsilon;

    // Leaner order: fewer truck-hours first, then less fuel, then fewer queue hours (owner
    // decision - fuel ranks above queue). Smaller tuple wins in every field it is compared on;
    // ties fall through to the next field.
    private static int CompareLeaner(ReplayScalars a, ReplayScalars b)
    {
        var c = a.TruckHours.CompareTo(b.TruckHours);
        if (c != 0) return c;
        c = a.FuelLitres.CompareTo(b.FuelLitres);
        if (c != 0) return c;
        return a.QueueHours.CompareTo(b.QueueHours);
    }

    /// <summary>How far a plan falls short of Original's mean tonnes to each destination, summed
    /// - zero exactly when MeetsDestinationConstraint would return true. Used only by the Leaner
    /// standdown repair search below, to hill-climb back to feasibility after a tentative
    /// standdown breaks it, before resuming the ordinary Leaner objective.</summary>
    private static double DestinationShortfall(ReplayScalars candidate, ReplayScalars original) =>
        Math.Max(0, original.CrusherTonnes - candidate.CrusherTonnes) +
        Math.Max(0, original.RomTonnes - candidate.RomTonnes) +
        Math.Max(0, original.WasteTonnes - candidate.WasteTonnes);

    /// <summary>Best-improvement local search from `original` (the shift as scheduled),
    /// searching for either "MoreOutput" (maximise mean total tonnes) or "Leaner" (minimise
    /// truck-hours, then fuel, then queue hours), subject to never scoring below the original
    /// plan's mean tonnes to any one destination. Evaluates every neighbour every iteration and
    /// takes the single best improving, feasible one, repeating until none improves - this is
    /// exhaustive best-improvement over the stated neighbourhood, not an approximation of it.
    ///
    /// One caching optimisation: a plan already scored earlier in this search (e.g. a swap that
    /// exactly reverses a move taken the previous iteration, landing back on a state already
    /// seen) is looked up instead of re-replayed. This changes no result versus scoring every
    /// neighbour fresh every time - it only skips recomputing an answer already known. `sharedCache`
    /// lets Leaner's standdown construction (below) reuse scores across many overlapping searches
    /// for the same shift instead of starting a fresh cache each time - same effect, more reuse.</summary>
    private static (Dictionary<int, int?> Plan, AggregatedScalars Scalars, List<LoaderOutcome> LoaderStats) LocalSearch(
        ShiftContext ctx, Dictionary<int, int?> original, AggregatedScalars originalScalars, string objective,
        Dictionary<string, (AggregatedScalars Scalars, List<LoaderOutcome> LoaderStats)>? sharedCache = null)
    {
        var cache = sharedCache ?? new Dictionary<string, (AggregatedScalars Scalars, List<LoaderOutcome> LoaderStats)>();

        (AggregatedScalars Scalars, List<LoaderOutcome> LoaderStats) ScoreCached(Dictionary<int, int?> plan)
        {
            var key = PlanKey(plan);
            if (cache.TryGetValue(key, out var hit)) return hit;
            var result = Score(ctx, plan);
            cache[key] = result;
            return result;
        }

        var current = new Dictionary<int, int?>(original);
        var (currentScalars, currentLoaders) = ScoreCached(current);

        var improved = true;
        while (improved)
        {
            improved = false;
            Dictionary<int, int?>? bestPlan = null;
            AggregatedScalars bestScalars = default;
            List<LoaderOutcome>? bestLoaders = null;

            foreach (var neighbour in Neighbours(ctx, current))
            {
                var (scalars, loaders) = ScoreCached(neighbour);
                if (!MeetsDestinationConstraint(scalars, originalScalars)) continue;

                bool better = objective == "MoreOutput"
                    ? scalars.Mean.TotalTonnes > (bestPlan == null ? currentScalars.Mean.TotalTonnes : bestScalars.Mean.TotalTonnes) + ConstraintEpsilon
                    : CompareLeaner(scalars.Mean, bestPlan == null ? currentScalars.Mean : bestScalars.Mean) < 0;

                if (better)
                {
                    bestPlan = neighbour;
                    bestScalars = scalars;
                    bestLoaders = loaders;
                }
            }

            if (bestPlan != null)
            {
                current = bestPlan;
                currentScalars = bestScalars;
                currentLoaders = bestLoaders;
                improved = true;
            }
        }

        return (current, currentScalars, currentLoaders!);
    }

    /// <summary>Best-improvement over the same neighbourhood as LocalSearch, but minimising
    /// DestinationShortfall instead of an objective, until the plan is feasible or no neighbour
    /// reduces the shortfall any further (stuck - the caller treats that as failure). Used to walk
    /// an infeasible tentative standdown back to feasibility before resuming the ordinary Leaner
    /// objective search from there. Ties in shortfall break on the Leaner objective, then on
    /// Neighbours' fixed enumeration order (via LINQ's stable OrderBy), so the result is
    /// deterministic.</summary>
    private static (Dictionary<int, int?> Plan, AggregatedScalars Scalars, List<LoaderOutcome> LoaderStats, bool Feasible) RepairToFeasible(
        ShiftContext ctx, Dictionary<int, int?> start, AggregatedScalars originalScalars,
        Dictionary<string, (AggregatedScalars Scalars, List<LoaderOutcome> LoaderStats)> cache)
    {
        (AggregatedScalars Scalars, List<LoaderOutcome> LoaderStats) ScoreCached(Dictionary<int, int?> plan)
        {
            var key = PlanKey(plan);
            if (cache.TryGetValue(key, out var hit)) return hit;
            var result = Score(ctx, plan);
            cache[key] = result;
            return result;
        }

        var current = start;
        var (currentScalars, currentLoaders) = ScoreCached(current);

        while (DestinationShortfall(currentScalars.Mean, originalScalars.Mean) > ConstraintEpsilon)
        {
            var ranked = Neighbours(ctx, current)
                .Select(n => (Plan: n, Result: ScoreCached(n)))
                .OrderBy(x => DestinationShortfall(x.Result.Scalars.Mean, originalScalars.Mean))
                .ThenBy(x => x.Result.Scalars.Mean, Comparer<ReplayScalars>.Create(CompareLeaner))
                .First();

            var bestShortfall = DestinationShortfall(ranked.Result.Scalars.Mean, originalScalars.Mean);
            var currentShortfall = DestinationShortfall(currentScalars.Mean, originalScalars.Mean);
            if (bestShortfall >= currentShortfall - ConstraintEpsilon)
                return (current, currentScalars, currentLoaders, false); // stuck - no neighbour makes it any less infeasible

            current = ranked.Plan;
            currentScalars = ranked.Result.Scalars;
            currentLoaders = ranked.Result.LoaderStats;
        }

        return (current, currentScalars, currentLoaders, true);
    }

    /// <summary>Leaner's plan, built by greedy standdown-and-repair from MoreOutput rather than by
    /// local search from Original directly. Local search alone gets stuck near Original: a single
    /// truck standdown, on its own, almost always drops one destination's tonnes below Original's
    /// floor (one fewer contributor to whatever it was hauling), and no other single move or swap
    /// can also fully cover that loss in the same step - so best-improvement from Original never
    /// finds an accepted standdown even when the fleet has real slack (as MoreOutput's ~24% tonnes
    /// headroom shows it usually does).
    ///
    /// Instead: start from MoreOutput (already feasible, already has that slack). Each round, try
    /// tentatively standing down every currently-working truck in turn; for each, run
    /// RepairToFeasible (reassign the remaining trucks' routes to cover the loss) then, once
    /// feasible, one more LocalSearch pass with the Leaner objective from there. Among the
    /// candidates that reach a feasible result, commit whichever has the best Leaner objective,
    /// but only if it actually beats the current plan's own objective - otherwise stop, since
    /// neither this nor any later round can do better (standing down an additional truck only
    /// gets harder as fewer working trucks remain to cover for it). Finish with one unconditional
    /// LocalSearch polish pass, same as MoreOutput gets.
    ///
    /// A single cache (`cache`) is shared across every standdown attempt, every repair, and the
    /// final polish, for this shift only - most attempts revisit overlapping neighbour states
    /// (the same "other truck, unchanged" positions keep recurring), so this avoids re-replaying
    /// an assignment already scored earlier in the same shift's Leaner construction. It changes no
    /// result versus an unshared cache, only how much repeat work is skipped.</summary>
    private static (Dictionary<int, int?> Plan, AggregatedScalars Scalars, List<LoaderOutcome> LoaderStats) BuildLeanerPlan(
        ShiftContext ctx, Dictionary<int, int?> moreOutputPlan, AggregatedScalars moreOutputScalars, List<LoaderOutcome> moreOutputLoaders,
        AggregatedScalars originalScalars)
    {
        var cache = new Dictionary<string, (AggregatedScalars Scalars, List<LoaderOutcome> LoaderStats)>();
        cache[PlanKey(moreOutputPlan)] = (moreOutputScalars, moreOutputLoaders);

        var current = moreOutputPlan;
        var currentScalars = moreOutputScalars;
        var currentLoaders = moreOutputLoaders;

        var improved = true;
        while (improved)
        {
            improved = false;
            Dictionary<int, int?>? bestPlan = null;
            AggregatedScalars bestScalars = default;
            List<LoaderOutcome>? bestLoaders = null;

            var workingTrucks = ctx.AvailableTruckIds.Where(id => current[id] != null).OrderBy(id => id).ToList();
            foreach (var truckId in workingTrucks)
            {
                var tentative = new Dictionary<int, int?>(current) { [truckId] = null };

                var (repairedPlan, repairedScalars, repairedLoaders, feasible) = RepairToFeasible(ctx, tentative, originalScalars, cache);
                if (!feasible) continue;

                var (polishedPlan, polishedScalars, polishedLoaders) = LocalSearch(ctx, repairedPlan, originalScalars, "Leaner", cache);
                if (!MeetsDestinationConstraint(polishedScalars, originalScalars)) continue; // polish must stay feasible too

                if (bestPlan == null || CompareLeaner(polishedScalars.Mean, bestScalars.Mean) < 0)
                {
                    bestPlan = polishedPlan;
                    bestScalars = polishedScalars;
                    bestLoaders = polishedLoaders;
                }
            }

            if (bestPlan != null && CompareLeaner(bestScalars.Mean, currentScalars.Mean) < 0)
            {
                current = bestPlan;
                currentScalars = bestScalars;
                currentLoaders = bestLoaders!;
                improved = true;
            }
        }

        // Final polish pass on whatever standing-down converged to (a no-op if the loop above's
        // last accepted candidate was already itself the result of a polish pass, since LocalSearch
        // starting from an already-locally-optimal plan makes no move).
        var (finalPlan, finalScalars, finalLoaders) = LocalSearch(ctx, current, originalScalars, "Leaner", cache);
        return (finalPlan, finalScalars, finalLoaders);
    }

    private static string PlanKey(Dictionary<int, int?> plan) =>
        string.Join(",", plan.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{kv.Value?.ToString() ?? "x"}"));

    // ------------------------------------------------------------------
    // Per-shift orchestration (pure - no DB access)
    // ------------------------------------------------------------------

    public record ShiftResult(ShiftKey Shift, OptimisedPlan Original, OptimisedPlan MoreOutput, OptimisedPlan Leaner);

    public static ShiftResult OptimiseShift(ShiftContext ctx, Dictionary<int, int?> originalRouteByTruckId, IReadOnlySet<int> unavailableTruckIds)
    {
        var (originalScalars, originalLoaders) = Score(ctx, originalRouteByTruckId);

        var (moreOutputPlan, moreOutputScalars, moreOutputLoaders) = LocalSearch(ctx, originalRouteByTruckId, originalScalars, "MoreOutput");
        var (leanerPlan, leanerScalars, leanerLoaders) = BuildLeanerPlan(ctx, moreOutputPlan, moreOutputScalars, moreOutputLoaders, originalScalars);

        OptimisedPlan Build(string planType, Dictionary<int, int?> plan, AggregatedScalars scalars, List<LoaderOutcome> loaders)
        {
            var assignments = new Dictionary<int, TruckAssignment>();
            foreach (var truckId in ctx.Fleet.Trucks.Select(t => t.Id))
            {
                var isUnavailable = unavailableTruckIds.Contains(truckId);
                var routeId = plan.TryGetValue(truckId, out var r) ? r : null;
                assignments[truckId] = new TruckAssignment { TruckId = truckId, RouteId = routeId, IsUnavailable = isUnavailable };
            }
            return new OptimisedPlan { PlanType = planType, Assignments = assignments, Scalars = scalars, LoaderStats = loaders };
        }

        var original = Build("Original", originalRouteByTruckId, originalScalars, originalLoaders);
        var moreOutput = Build("MoreOutput", moreOutputPlan, moreOutputScalars, moreOutputLoaders);
        var leaner = Build("Leaner", leanerPlan, leanerScalars, leanerLoaders);

        return new ShiftResult(ctx.Shift, original, moreOutput, leaner);
    }
}
