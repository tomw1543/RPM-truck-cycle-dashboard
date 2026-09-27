// HaulCycle Insights - event-driven joint simulation engine.
//
// Replaces the old per-truck, run-to-completion simulation with one engine that advances
// every truck together in time order, sharing loaders as a contended resource. Both history
// mode and live mode (Program.cs) drive the same TruckTimeline objects through this engine;
// the only difference is how far ahead each mode is allowed to run before pausing.
//
// A loader is represented by LoaderPlanner: it tracks each loader's FreeAt time (when it can
// next start a load) plus every LoaderDelay (shift-change handover, full stop; loader spike,
// half speed) that has been planned so far. TruckTimeline.Next() calls into it (through
// Simulator.BuildCycle) whenever a truck reaches the front of a loader's queue.
//
// Determinism: PickNext always chooses the truck with the smallest Cursor, ties broken by
// truck id, so a given seed always processes events in the same order.

record LoaderDelayRow(int LoaderId, DateTime StartTime, DateTime EndTime, string Reason, bool IsPlanned, double RateFactor);

/// <summary>Plans loader-side events - shift-change handover (planted problem #3) and loader
/// spikes (planted problem #4) - and tracks each loader's FreeAt time as cycles are built
/// against it. Shared across all trucks, the same way the old LoaderSpikeSchedule was.</summary>
class LoaderPlanner
{
    private readonly List<Loader> _loaders;
    private readonly Random _rng;
    private readonly List<LoaderDelayRow> _delays = new();
    private readonly Dictionary<int, DateTime> _freeAt = new();
    private DateTime _plannedUntil;

    /// <summary>Raised the moment a new delay is planned (used by live mode to queue it for
    /// insertion - see Program.cs). Not raised retroactively for delays already in AllDelays.</summary>
    public event Action<LoaderDelayRow>? DelayPlanned;

    public LoaderPlanner(List<Loader> loaders, Random rng, DateTime start)
    {
        _loaders = loaders;
        _rng = rng;
        _plannedUntil = start;
    }

    /// <summary>Builds a planner already "planned" through plannedUntil from a fixed list of
    /// loader delays (typically read back from dbo.LoaderDelays), so EnsurePlanned never draws
    /// any further loader-side randomness for a replay that stays within that window. Used by
    /// the optimiser: loader delays for a shift are a fixed input shared by every plan variant
    /// and every replay seed, never redrawn per plan (see SimulationEngine.ReplayShift). The
    /// FreeAt dictionary starts empty either way - a replay always starts each loader "free",
    /// the same convention --replay-selfcheck already uses.</summary>
    public static LoaderPlanner FromExisting(List<Loader> loaders, IEnumerable<LoaderDelayRow> existingDelays, DateTime plannedUntil, Random rng)
    {
        var planner = new LoaderPlanner(loaders, rng, plannedUntil);
        foreach (var d in existingDelays)
            planner._delays.Add(d);
        return planner;
    }

    public IReadOnlyList<LoaderDelayRow> AllDelays => _delays;

    /// <summary>Deep-enough copy for independent replay: the clone gets its own delay list and
    /// FreeAt dictionary, so mutating one instance (via EnsurePlanned or SetFreeAt) never
    /// affects the other. Loaders are shared by reference (read-only reference data) but the
    /// clone gets its own Random, since further EnsurePlanned calls draw from it - pass a
    /// freshly seeded one if the two replays must not diverge only because of loader-side
    /// randomness, or the same instance if you want them to share that stream on purpose.
    ///
    /// For a replay, the caller is expected to call EnsurePlanned(shiftEnd) on the base planner
    /// before taking any clones, so no clone ever needs to draw again within that shift - the
    /// rng passed here only matters if the planner is later asked to plan past shiftEnd. When it
    /// does matter, seed it with SimulationEngine.SeededRandom(seed, shiftStart,
    /// SimulationEngine.LoaderEntityId) so loader-side randomness is reproducible per (seed,
    /// shift) the same way per-truck streams are.</summary>
    public LoaderPlanner Clone(Random rng)
    {
        var clone = new LoaderPlanner(_loaders, rng, _plannedUntil);
        clone._delays.AddRange(_delays);
        foreach (var kv in _freeAt) clone._freeAt[kv.Key] = kv.Value;
        return clone;
    }

    public DateTime FreeAt(int loaderId) => _freeAt.TryGetValue(loaderId, out var t) ? t : DateTime.MinValue;

    public void SetFreeAt(int loaderId, DateTime t) => _freeAt[loaderId] = t;

    /// <summary>Pushes t forward past any full-stop (handover, RateFactor 0.00) window on this
    /// loader that covers it. A truck arriving during a handover simply waits for it to end.</summary>
    public DateTime SkipPastFullStops(int loaderId, DateTime t)
    {
        while (true)
        {
            var stop = _delays.FirstOrDefault(d =>
                d.LoaderId == loaderId && d.RateFactor == 0.00 && d.StartTime <= t && t < d.EndTime);
            if (stop == null) break;
            t = stop.EndTime;
        }
        return t;
    }

    /// <summary>The loader's rate multiplier at instant t: 1.0 normally, 0.50 during a spike
    /// (so load time doubles). Handovers are handled separately by SkipPastFullStops.</summary>
    public double RateFactorAt(int loaderId, DateTime t)
    {
        var spike = _delays.FirstOrDefault(d =>
            d.LoaderId == loaderId && d.RateFactor > 0.0 && d.RateFactor < 1.0 && d.StartTime <= t && t < d.EndTime);
        return spike?.RateFactor ?? 1.0;
    }

    public void EnsurePlanned(DateTime upTo)
    {
        if (_plannedUntil >= upTo) return;

        // Shift-change handover: every loader stops for ~15 minutes at every 06:00/18:00
        // boundary in range (planted problem #3, loader-side).
        for (var boundary = FloorToShiftBoundary(_plannedUntil); boundary < upTo; boundary = boundary.AddHours(12))
        {
            if (boundary < _plannedUntil) continue;
            foreach (var loader in _loaders)
            {
                var duration = Math.Max(5, Normal(_rng, 15, 2));
                Add(new LoaderDelayRow(loader.Id, boundary, boundary.AddMinutes(duration), "Shift change handover", true, 0.00));
            }
        }

        // Loader spike: roughly once per loader per day, half speed for 30-90 minutes
        // (planted problem #4).
        for (var day = _plannedUntil.Date; day < upTo; day = day.AddDays(1))
        {
            foreach (var loader in _loaders)
            {
                if (_rng.NextDouble() < 0.9) // "roughly" 1 per loader per day
                {
                    var start = day.AddMinutes(_rng.NextDouble() * 24 * 60);
                    var duration = 30 + _rng.NextDouble() * 60; // 30-90 minutes
                    Add(new LoaderDelayRow(loader.Id, start, start.AddMinutes(duration), "Loader spike", false, 0.50));
                }
            }
        }

        _plannedUntil = upTo;
    }

    private void Add(LoaderDelayRow row)
    {
        _delays.Add(row);
        DelayPlanned?.Invoke(row);
    }

    private static DateTime FloorToShiftBoundary(DateTime t)
    {
        var six = t.Date.AddHours(6);
        var eighteen = t.Date.AddHours(18);
        if (t >= eighteen) return eighteen;
        if (t >= six) return six;
        return t.Date.AddDays(-1).AddHours(18);
    }

    private static double Normal(Random rng, double mean, double sd)
    {
        var u1 = 1.0 - rng.NextDouble();
        var u2 = rng.NextDouble();
        return mean + sd * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}

/// <summary>The joint-simulation picker plus a replay entry point for a future optimiser.
/// No DB access anywhere in this class.</summary>
static class SimulationEngine
{
    /// <summary>Entity id passed to SeededRandom for loader-side randomness (LoaderPlanner's
    /// EnsurePlanned draws), which is not truck-specific. Any id outside the truck id range
    /// would do; -1 is used because no truck ever has this id.</summary>
    public const int LoaderEntityId = -1;

    /// <summary>Deterministic seed for one entity's (a truck, or LoaderEntityId for loader-side
    /// randomness) Random stream during a replay of one shift. Combines the data/replay seed,
    /// the shift's start instant, and the entity id, so the resulting stream depends on nothing
    /// else - not on which other trucks are in the replay, not on the order timelines are
    /// created in, not on wall-clock time. Two replays of the same shift with the same seed
    /// always give one entity the same stream, even if every other entity's assignment changes -
    /// this is what lets the optimiser move one truck's route without disturbing any other
    /// truck's draws.
    ///
    /// Deliberately hand-rolled instead of HashCode.Combine: HashCode.Combine randomizes its
    /// output with a new seed every process run (by design, for DoS resistance), which would
    /// make a replay's outcome different every time the generator runs even for the same inputs -
    /// exactly what this method must not do.
    ///
    /// Not used by history or live mode: both keep consuming one shared Random the way they
    /// always have (see the note on Simulator), so seed-42 history output is unaffected by this
    /// method's existence.</summary>
    public static Random SeededRandom(int seed, DateTime shiftStart, int entityId)
    {
        unchecked
        {
            long h = 17;
            h = h * 31 + seed;
            h = h * 31 + shiftStart.Ticks;
            h = h * 31 + entityId;
            var folded = (int)(h ^ (h >> 32));
            return new Random(folded);
        }
    }

    /// <summary>Picks the eligible truck whose Cursor is earliest, ties broken by truck id -
    /// the one fixed tie-break rule the whole engine relies on for determinism. Returns null
    /// if no timeline is eligible.</summary>
    public static TruckTimeline? PickNext(IReadOnlyList<TruckTimeline> timelines, Func<TruckTimeline, bool> eligible)
    {
        TruckTimeline? best = null;
        foreach (var t in timelines)
        {
            if (!eligible(t)) continue;
            if (best == null || t.Cursor < best.Cursor || (t.Cursor == best.Cursor && t.Truck.Id < best.Truck.Id))
                best = t;
        }
        return best;
    }

    /// <summary>Runs every truck's timeline forward to horizon, always advancing whichever
    /// truck's cursor is earliest next (see PickNext), so loader FIFO order reflects genuine
    /// arrival order across the whole fleet. Used by history mode for the whole window.</summary>
    public static List<object> RunToHorizon(List<TruckTimeline> timelines, DateTime horizon)
    {
        var events = new List<object>();
        while (true)
        {
            var next = PickNext(timelines, t => t.Cursor < horizon);
            if (next == null) break;
            var ev = next.Next(horizon);
            if (ev != null) events.Add(ev);
        }
        return events;
    }

    /// <summary>Replay entry point for a future optimiser: simulate every given truck through
    /// one shift on a fixed route/loader assignment, sharing the same loader-contention model
    /// as history/live mode. Nothing is read from or written to a database.
    ///
    /// Every truck in routeByTruckId is advanced together through RunToHorizon, using the same
    /// PickNext tie-break (earliest cursor, ties by truck id) that history mode uses for the
    /// whole window - so a loader's FIFO queue reflects genuine cross-truck arrival order for
    /// the shift, not one truck's entire shift finishing before the next truck is considered.
    /// A null route means the truck is unavailable for the shift: its timeline idles straight
    /// to shiftEnd with no cycles, the same as the scheduler-driven flow does today.
    ///
    /// Each truck starts at shiftStart; if a known delay (from knownDelaysByTruckId) is already
    /// pending at that instant, TruckTimeline's own pending-delay handling takes over exactly as
    /// it does for history/live mode - the delay runs first (for its full planned duration) and
    /// the truck's first cycle begins only once it ends. This method adds no separate mid-delay
    /// logic of its own.
    ///
    /// Mutates loaderPlanner in place (FreeAt state and any newly planned handover/spike delays
    /// end up in it after the call), the same way RunToHorizon always does. To replay the same
    /// starting state under a second, independent assignment, pass loaderPlanner.Clone(...) for
    /// one of the two calls - the two replays must not share a LoaderPlanner instance or one
    /// will see loader contention from the other's cycles. Call EnsurePlanned(shiftEnd) on the
    /// base LoaderPlanner (or its clones) BEFORE any replay of the shift, so every replay and
    /// every plan variant sees the exact same loader-side delays (handover, spikes) as fixed
    /// input - EnsurePlanned(upTo &lt;= shiftEnd) inside TruckTimeline.Next() is then a no-op for
    /// the rest of the shift, and loader delays are never redrawn differently per plan.
    ///
    /// Determinism: `seed` (together with `shiftStart`) is fed through SeededRandom to build one
    /// fresh, independent Random per truck, keyed only by that truck's own id - not by iteration
    /// order, not by which other trucks are in routeByTruckId, not by their routes. Moving one
    /// truck to a different route, or dropping another truck out of routeByTruckId entirely,
    /// never changes any other truck's draw sequence. Give loader-side randomness (LoaderPlanner)
    /// the same treatment by seeding it with SeededRandom(seed, shiftStart, SimulationEngine.LoaderEntityId)
    /// wherever it is constructed for a replay.
    ///
    /// Returns cycles and delays together, as RunToHorizon does; filter with OfType&lt;CycleRow&gt;()
    /// if only cycles are wanted. Not called anywhere yet except --replay-selfcheck - wired up
    /// for real when the optimiser slice lands.</summary>
    public static List<object> ReplayShift(
        Fleet fleet, Simulator sim, DateTime shiftStart, DateTime shiftEnd,
        IReadOnlyDictionary<int, int?> routeByTruckId,
        IReadOnlyDictionary<int, List<PendingDelay>> knownDelaysByTruckId,
        LoaderPlanner loaderPlanner, int seed)
    {
        var shiftDate = DateOnly.FromDateTime(shiftStart.Date);
        var shiftName = shiftStart.Hour == 6 ? "Day" : "Night";

        // One fixed ScheduleRow per truck for this shift, built up front so every timeline's
        // lookup sees the same assignment regardless of simulation order.
        var schedules = new Dictionary<int, ScheduleRow>();
        foreach (var (truckId, routeId) in routeByTruckId)
        {
            if (routeId == null)
            {
                schedules[truckId] = new ScheduleRow(shiftDate, shiftName, truckId, null, null, null, null, "Not assigned");
                continue;
            }
            var route = fleet.RouteById(routeId.Value);
            schedules[truckId] = new ScheduleRow(shiftDate, shiftName, truckId, route.Id, route.LoaderId, null, null, null);
        }

        ScheduleRow? Lookup(int truckId, DateOnly sd, string sn) =>
            sd == shiftDate && sn == shiftName && schedules.TryGetValue(truckId, out var row) ? row : null;

        var timelines = new List<TruckTimeline>();
        foreach (var truckId in routeByTruckId.Keys)
        {
            var truck = fleet.Trucks.First(t => t.Id == truckId);
            var knownDelays = knownDelaysByTruckId.TryGetValue(truckId, out var d) ? d : new List<PendingDelay>();
            var truckRng = SeededRandom(seed, shiftStart, truckId);
            timelines.Add(new TruckTimeline(truck, shiftStart, truckRng, sim, loaderPlanner, fleet, Lookup, knownDelays));
        }

        return RunToHorizon(timelines, shiftEnd);
    }
}
