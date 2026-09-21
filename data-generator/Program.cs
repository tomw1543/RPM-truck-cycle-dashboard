// HaulCycle Insights - synthetic data generator.
// Independent learning project. All numbers are made up (illustrative only).
//
// Usage (from the repo root):
//   dotnet run --project data-generator -- --reset-schema     -> (re)create all tables from db/schema.sql
//   dotnet run --project data-generator                       -> wipe data, load 30 days of history (seed 42)
//   dotnet run --project data-generator -- --days 60           -> 60 days of history
//   dotnet run --project data-generator -- --seed 7            -> different (but repeatable) random data
//   dotnet run --project data-generator -- --live --speed 10   -> keep existing data, stream new cycles/delays
//
// The connection string comes from the HAUL_DB_CONN environment variable. There is no
// fallback - set it before running (see README.md). Never commit a real password.
//
// All timestamps are "mine time": Australia/Brisbane (UTC+10, no daylight saving).

using System.Data;
using Microsoft.Data.SqlClient;

var connString = Environment.GetEnvironmentVariable("HAUL_DB_CONN");
if (string.IsNullOrWhiteSpace(connString))
{
    Console.Error.WriteLine("HAUL_DB_CONN is not set.");
    Console.Error.WriteLine("Example (local Docker SQL Server):");
    Console.Error.WriteLine("  Server=localhost,1433;Database=HaulCycleInsights;User Id=sa;Password=<from .env>;TrustServerCertificate=True");
    return 1;
}

var days = GetIntArg("--days", 30);
var seed = GetIntArg("--seed", 42);
var speed = GetDoubleArg("--speed", 1.0);
var live = args.Contains("--live");
var resetSchemaOnly = args.Contains("--reset-schema");

if (resetSchemaOnly)
{
    await using var resetConn = new SqlConnection(connString);
    await resetConn.OpenAsync();
    await ResetSchemaAsync(resetConn);
    Console.WriteLine("Schema reset complete.");
    return 0;
}

var fleet = Fleet.Create();
// History uses a fixed seed so the data (and your README numbers) are repeatable.
// Live mode uses a fresh random stream so it doesn't replay the same values.
var rng = live ? new Random() : new Random(seed);
var sim = new Simulator(fleet, rng);

if (live)
    await RunLiveAsync();
else
    await RunHistoryAsync();

return 0;

// ---------------------------------------------------------------------------
// Modes
// ---------------------------------------------------------------------------

async Task RunHistoryAsync()
{
    var to = MineTime.Now;
    var from = to.Date.AddDays(-days);

    Console.WriteLine($"Generating {days} days of cycles/delays for {fleet.Trucks.Count} trucks (seed {seed})...");

    await using var conn = new SqlConnection(connString);
    await conn.OpenAsync();

    if (!await TablesExistAsync(conn))
    {
        Console.WriteLine("Required tables are missing; running schema reset automatically.");
        await ResetSchemaAsync(conn);
    }

    // Loader queue spikes are planned once, fleet-wide, before any truck is simulated,
    // so the rng consumption order stays fixed for a given seed.
    var spikes = new LoaderSpikeSchedule(fleet.Loaders, rng, from);
    spikes.EnsurePlanned(to);

    var cycles = new List<CycleRow>();
    var delays = new List<DelayRow>();

    foreach (var truck in fleet.Trucks)
    {
        var cursor0 = from.AddMinutes(rng.Next(0, 15)); // stagger truck start times
        var timeline = new TruckTimeline(truck, cursor0, rng, sim, spikes);
        timeline.EnsurePlanned(to);

        while (timeline.Cursor < to)
        {
            var ev = timeline.Next(to);
            if (ev is CycleRow cr) cycles.Add(cr);
            else if (ev is DelayRow dr) delays.Add(dr);
        }
    }

    await ResetAndSeedReferenceDataAsync(conn);
    await BulkInsertCyclesAsync(conn, cycles);
    await BulkInsertDelaysAsync(conn, delays);

    Console.WriteLine($"Done. Inserted {cycles.Count:N0} cycles and {delays.Count:N0} delays.");
}

async Task RunLiveAsync()
{
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    await using var conn = new SqlConnection(connString);
    await conn.OpenAsync();

    if (!await TablesExistAsync(conn))
    {
        Console.Error.WriteLine("Tables are missing. Run --reset-schema and a history load first.");
        return;
    }

    var latest = await GetLatestEndPerTruckAsync(conn);
    var now = MineTime.Now;
    var initialCursor = fleet.Trucks.ToDictionary(
        t => t.Id,
        t => latest.TryGetValue(t.Id, out var end) && end.HasValue ? end.Value : now);

    var simClockStart = initialCursor.Values.DefaultIfEmpty(now).Max();
    var spikes = new LoaderSpikeSchedule(fleet.Loaders, rng, simClockStart);
    var timelines = fleet.Trucks.ToDictionary(
        t => t.Id,
        t => new TruckTimeline(t, initialCursor[t.Id], rng, sim, spikes));

    var pending = new Dictionary<int, PendingEvent>();
    var wallStart = DateTime.UtcNow;

    Console.WriteLine($"Live mode: speed {speed:0.#}x, sim clock starts at {simClockStart:yyyy-MM-dd HH:mm:ss} mine time. Press Ctrl+C to stop.");

    try
    {
        while (!cts.IsCancellationRequested)
        {
            foreach (var truck in fleet.Trucks)
            {
                if (!pending.ContainsKey(truck.Id))
                {
                    var ev = timelines[truck.Id].Next();
                    pending[truck.Id] = new PendingEvent(ev, EndOf(ev));
                }
            }

            var simClock = simClockStart + TimeSpan.FromTicks((long)((DateTime.UtcNow - wallStart).Ticks * speed));

            foreach (var truck in fleet.Trucks)
            {
                if (pending.TryGetValue(truck.Id, out var pe) && pe.End <= simClock)
                {
                    if (pe.Event is CycleRow cr)
                    {
                        await InsertOneCycleAsync(conn, cr);
                        Console.WriteLine($"{simClock:HH:mm:ss}  truck {cr.TruckId,2}  cycle {Simulator.TotalMin(cr):0.0} min  payload {cr.PayloadTonnes:0.0} t");
                    }
                    else if (pe.Event is DelayRow dr)
                    {
                        await InsertOneDelayAsync(conn, dr);
                        Console.WriteLine($"{simClock:HH:mm:ss}  truck {dr.TruckId,2}  delay  {dr.Reason} ({(dr.EndTime - dr.StartTime).TotalMinutes:0.0} min)");
                    }
                    pending.Remove(truck.Id);
                }
            }

            await Task.Delay(200, cts.Token);
        }
    }
    catch (OperationCanceledException)
    {
        // Ctrl+C - fine.
    }

    Console.WriteLine("Stopped.");
}

// ---------------------------------------------------------------------------
// Schema
// ---------------------------------------------------------------------------

async Task<bool> TablesExistAsync(SqlConnection conn)
{
    await using var cmd = new SqlCommand(
        "SELECT COUNT(*) FROM sys.tables WHERE name IN ('Trucks','Loaders','Routes','Cycles','Delays');", conn);
    var count = (int)(await cmd.ExecuteScalarAsync())!;
    return count == 5;
}

async Task ResetSchemaAsync(SqlConnection conn)
{
    var path = Path.Combine(AppContext.BaseDirectory, "schema.sql");
    if (!File.Exists(path))
        path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "db", "schema.sql");
    if (!File.Exists(path))
        throw new FileNotFoundException("Could not find schema.sql next to the built executable or at ../../../../db/schema.sql.");

    var lines = await File.ReadAllLinesAsync(path);
    var batches = new List<string>();
    var current = new System.Text.StringBuilder();

    foreach (var line in lines)
    {
        if (line.Trim().Equals("GO", StringComparison.OrdinalIgnoreCase))
        {
            if (current.Length > 0)
            {
                batches.Add(current.ToString());
                current.Clear();
            }
        }
        else
        {
            current.AppendLine(line);
        }
    }
    if (current.Length > 0)
        batches.Add(current.ToString());

    foreach (var batch in batches)
    {
        if (!string.IsNullOrWhiteSpace(batch))
            await ExecAsync(conn, batch);
    }
}

// ---------------------------------------------------------------------------
// Database helpers
// ---------------------------------------------------------------------------

async Task ResetAndSeedReferenceDataAsync(SqlConnection conn)
{
    // Cycles/Delays are not referenced by any other table, so TRUNCATE works (and resets identities).
    await ExecAsync(conn, "TRUNCATE TABLE dbo.Delays;");
    await ExecAsync(conn, "TRUNCATE TABLE dbo.Cycles;");
    await ExecAsync(conn, "DELETE FROM dbo.Routes;");
    await ExecAsync(conn, "DELETE FROM dbo.Loaders;");
    await ExecAsync(conn, "DELETE FROM dbo.Trucks;");

    foreach (var t in fleet.Trucks)
    {
        await using var cmd = new SqlCommand(
            "INSERT INTO dbo.Trucks (TruckId, Name, CapacityTonnes) VALUES (@id, @name, @cap);", conn);
        cmd.Parameters.AddWithValue("@id", t.Id);
        cmd.Parameters.AddWithValue("@name", t.Name);
        cmd.Parameters.AddWithValue("@cap", (decimal)t.CapacityTonnes);
        await cmd.ExecuteNonQueryAsync();
    }

    foreach (var l in fleet.Loaders)
    {
        await using var cmd = new SqlCommand(
            "INSERT INTO dbo.Loaders (LoaderId, Name) VALUES (@id, @name);", conn);
        cmd.Parameters.AddWithValue("@id", l.Id);
        cmd.Parameters.AddWithValue("@name", l.Name);
        await cmd.ExecuteNonQueryAsync();
    }

    foreach (var r in fleet.Routes)
    {
        await using var cmd = new SqlCommand(
            "INSERT INTO dbo.Routes (RouteId, Name, DistanceKm, GradePercent) VALUES (@id, @name, @dist, @grade);", conn);
        cmd.Parameters.AddWithValue("@id", r.Id);
        cmd.Parameters.AddWithValue("@name", r.Name);
        cmd.Parameters.AddWithValue("@dist", (decimal)r.DistanceKm);
        cmd.Parameters.AddWithValue("@grade", (decimal)r.GradePercent);
        await cmd.ExecuteNonQueryAsync();
    }
}

async Task BulkInsertCyclesAsync(SqlConnection conn, List<CycleRow> rows)
{
    var table = new DataTable();
    table.Columns.Add("TruckId", typeof(int));
    table.Columns.Add("LoaderId", typeof(int));
    table.Columns.Add("RouteId", typeof(int));
    table.Columns.Add("StartTime", typeof(DateTime));
    table.Columns.Add("LoadMin", typeof(decimal));
    table.Columns.Add("HaulMin", typeof(decimal));
    table.Columns.Add("DumpMin", typeof(decimal));
    table.Columns.Add("ReturnMin", typeof(decimal));
    table.Columns.Add("QueueMin", typeof(decimal));
    table.Columns.Add("PayloadTonnes", typeof(decimal));
    table.Columns.Add("FuelLitres", typeof(decimal));

    foreach (var r in rows)
    {
        table.Rows.Add(
            r.TruckId, r.LoaderId, r.RouteId, r.StartTime,
            (decimal)r.LoadMin, (decimal)r.HaulMin, (decimal)r.DumpMin,
            (decimal)r.ReturnMin, (decimal)r.QueueMin,
            (decimal)r.PayloadTonnes, (decimal)r.FuelLitres);
    }

    using var bulk = new SqlBulkCopy(conn)
    {
        DestinationTableName = "dbo.Cycles",
        BatchSize = 5000
    };
    // Map by name so the identity column (CycleId) is left for SQL Server to fill.
    foreach (DataColumn c in table.Columns)
        bulk.ColumnMappings.Add(c.ColumnName, c.ColumnName);

    await bulk.WriteToServerAsync(table);
}

async Task BulkInsertDelaysAsync(SqlConnection conn, List<DelayRow> rows)
{
    var table = new DataTable();
    table.Columns.Add("TruckId", typeof(int));
    table.Columns.Add("StartTime", typeof(DateTime));
    table.Columns.Add("EndTime", typeof(DateTime));
    table.Columns.Add("Reason", typeof(string));
    table.Columns.Add("IsPlanned", typeof(bool));

    foreach (var r in rows)
        table.Rows.Add(r.TruckId, r.StartTime, r.EndTime, r.Reason, r.IsPlanned);

    using var bulk = new SqlBulkCopy(conn)
    {
        DestinationTableName = "dbo.Delays",
        BatchSize = 5000
    };
    foreach (DataColumn c in table.Columns)
        bulk.ColumnMappings.Add(c.ColumnName, c.ColumnName);

    await bulk.WriteToServerAsync(table);
}

async Task InsertOneCycleAsync(SqlConnection conn, CycleRow r)
{
    await using var cmd = new SqlCommand(@"
        INSERT INTO dbo.Cycles
            (TruckId, LoaderId, RouteId, StartTime, LoadMin, HaulMin, DumpMin, ReturnMin, QueueMin, PayloadTonnes, FuelLitres)
        VALUES
            (@truck, @loader, @route, @start, @load, @haul, @dump, @ret, @queue, @payload, @fuel);", conn);
    cmd.Parameters.AddWithValue("@truck", r.TruckId);
    cmd.Parameters.AddWithValue("@loader", r.LoaderId);
    cmd.Parameters.AddWithValue("@route", r.RouteId);
    cmd.Parameters.AddWithValue("@start", r.StartTime);
    cmd.Parameters.AddWithValue("@load", (decimal)r.LoadMin);
    cmd.Parameters.AddWithValue("@haul", (decimal)r.HaulMin);
    cmd.Parameters.AddWithValue("@dump", (decimal)r.DumpMin);
    cmd.Parameters.AddWithValue("@ret", (decimal)r.ReturnMin);
    cmd.Parameters.AddWithValue("@queue", (decimal)r.QueueMin);
    cmd.Parameters.AddWithValue("@payload", (decimal)r.PayloadTonnes);
    cmd.Parameters.AddWithValue("@fuel", (decimal)r.FuelLitres);
    await cmd.ExecuteNonQueryAsync();
}

async Task InsertOneDelayAsync(SqlConnection conn, DelayRow r)
{
    await using var cmd = new SqlCommand(@"
        INSERT INTO dbo.Delays (TruckId, StartTime, EndTime, Reason, IsPlanned)
        VALUES (@truck, @start, @end, @reason, @planned);", conn);
    cmd.Parameters.AddWithValue("@truck", r.TruckId);
    cmd.Parameters.AddWithValue("@start", r.StartTime);
    cmd.Parameters.AddWithValue("@end", r.EndTime);
    cmd.Parameters.AddWithValue("@reason", r.Reason);
    cmd.Parameters.AddWithValue("@planned", r.IsPlanned);
    await cmd.ExecuteNonQueryAsync();
}

async Task<Dictionary<int, DateTime?>> GetLatestEndPerTruckAsync(SqlConnection conn)
{
    var result = new Dictionary<int, DateTime?>();
    await using var cmd = new SqlCommand(@"
        SELECT TruckId, MAX(EndTime) AS LatestEnd
        FROM (
            SELECT TruckId,
                   DATEADD(MINUTE, CAST(LoadMin + HaulMin + DumpMin + ReturnMin + QueueMin AS FLOAT), StartTime) AS EndTime
            FROM dbo.Cycles
            UNION ALL
            SELECT TruckId, EndTime FROM dbo.Delays
        ) x
        GROUP BY TruckId;", conn);
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
        result[reader.GetInt32(0)] = reader.IsDBNull(1) ? (DateTime?)null : reader.GetDateTime(1);
    return result;
}

async Task ExecAsync(SqlConnection conn, string sql)
{
    await using var cmd = new SqlCommand(sql, conn);
    cmd.CommandTimeout = 60;
    await cmd.ExecuteNonQueryAsync();
}

int GetIntArg(string name, int fallback)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var v) ? v : fallback;
}

double GetDoubleArg(string name, double fallback)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length && double.TryParse(args[i + 1], out var v) ? v : fallback;
}

static DateTime EndOf(object ev) =>
    ev is CycleRow c ? c.StartTime.AddMinutes(Simulator.TotalMin(c)) : ((DelayRow)ev).EndTime;

// ---------------------------------------------------------------------------
// Types (must come after the top-level statements above)
// ---------------------------------------------------------------------------

/// <summary>Mine time = Australia/Brisbane (UTC+10, no daylight saving). All stored times are mine time.</summary>
static class MineTime
{
    public static readonly TimeZoneInfo Zone = Resolve();

    public static DateTime Now => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Zone).DateTime;

    private static TimeZoneInfo Resolve()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Australia/Brisbane"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("E. Australia Standard Time"); }
    }
}

record Truck(int Id, string Name, double CapacityTonnes, bool Underloaded);
record Loader(int Id, string Name);
record Route(int Id, string Name, double DistanceKm, double GradePercent, double SlowFactor);

record CycleRow(
    int TruckId, int LoaderId, int RouteId, DateTime StartTime,
    double LoadMin, double HaulMin, double DumpMin, double ReturnMin, double QueueMin,
    double PayloadTonnes, double FuelLitres);

record DelayRow(int TruckId, DateTime StartTime, DateTime EndTime, string Reason, bool IsPlanned);

record PendingEvent(object Event, DateTime End);

/// <summary>A planned or unplanned delay before it has been "placed" on a truck's timeline.
/// RequestedStart is when it should ideally start; if a cycle is in progress at that time,
/// TruckTimeline starts it as soon as that cycle ends instead.</summary>
record PendingDelay(DateTime RequestedStart, double DurationMin, string Reason, bool IsPlanned);

class Fleet
{
    public List<Truck> Trucks { get; } = new();
    public List<Loader> Loaders { get; } = new();
    public List<Route> Routes { get; } = new();

    public static Fleet Create()
    {
        var f = new Fleet();

        // 12 trucks. T07 is the "planted" underloaded truck. The dashboard should discover this.
        // (The flag lives only in this program - it is NOT stored in the database.)
        for (var i = 1; i <= 12; i++)
            f.Trucks.Add(new Truck(i, $"T{i:00}", 220, Underloaded: i == 7));

        for (var i = 1; i <= 3; i++)
            f.Loaders.Add(new Loader(i, $"L{i}"));

        f.Routes.Add(new Route(1, "Pit to ROM pad", 3.2, 6, 1.00));
        // Planted problem #2: a slow ramp. Route 2 hauls take ~15% longer than physics says they should.
        f.Routes.Add(new Route(2, "Pit to waste dump", 4.8, 8, 1.15));
        f.Routes.Add(new Route(3, "Pit to crusher", 2.1, 5, 1.00));

        return f;
    }
}

/// <summary>Plans loader queue spikes (planted problem #4): roughly one 30-90 minute
/// spike per loader per day, during which queue mean at that loader rises to ~5 min.</summary>
class LoaderSpikeSchedule
{
    private readonly List<Loader> _loaders;
    private readonly Random _rng;
    private readonly List<(int LoaderId, DateTime Start, DateTime End)> _spikes = new();
    private DateTime _plannedUntil;

    public LoaderSpikeSchedule(List<Loader> loaders, Random rng, DateTime start)
    {
        _loaders = loaders;
        _rng = rng;
        _plannedUntil = start.Date;
    }

    public void EnsurePlanned(DateTime upTo)
    {
        if (_plannedUntil >= upTo) return;

        for (var day = _plannedUntil.Date; day < upTo; day = day.AddDays(1))
        {
            foreach (var loader in _loaders)
            {
                if (_rng.NextDouble() < 0.9) // "roughly" 1 per loader per day
                {
                    var start = day.AddMinutes(_rng.NextDouble() * 24 * 60);
                    var duration = 30 + _rng.NextDouble() * 60; // 30-90 minutes
                    _spikes.Add((loader.Id, start, start.AddMinutes(duration)));
                }
            }
        }
        _plannedUntil = upTo;
    }

    public bool IsInSpike(int loaderId, DateTime t) =>
        _spikes.Any(s => s.LoaderId == loaderId && t >= s.Start && t < s.End);
}

/// <summary>Plans planned and unplanned delays for one truck over a time window.</summary>
static class DelayPlanner
{
    public static List<PendingDelay> Plan(Random rng, DateTime from, DateTime to)
    {
        var list = new List<PendingDelay>();

        // Shift boundaries: Day 06:00-18:00, Night 18:00-06:00, mine time.
        for (var shiftStart = FloorToShiftBoundary(from); shiftStart < to; shiftStart = shiftStart.AddHours(12))
        {
            if (shiftStart >= from)
                list.Add(new PendingDelay(shiftStart, Math.Max(5, Normal(rng, 15, 2)), "Shift change handover", true));

            var refuelAt = shiftStart.AddMinutes(rng.NextDouble() * 12 * 60);
            if (refuelAt >= from && refuelAt < to)
                list.Add(new PendingDelay(refuelAt, Math.Max(5, Normal(rng, 15, 3)), "Refuel", true));

            var cribAt = shiftStart.AddMinutes(rng.NextDouble() * 12 * 60);
            if (cribAt >= from && cribAt < to)
                list.Add(new PendingDelay(cribAt, Math.Max(10, Normal(rng, 30, 5)), "Crib break", true));
        }

        // Scheduled maintenance: about once a week per truck (~4 hours).
        for (var weekStart = from.Date; weekStart < to; weekStart = weekStart.AddDays(7))
        {
            if (rng.NextDouble() < 0.85)
            {
                var at = weekStart.AddMinutes(rng.NextDouble() * 7 * 24 * 60);
                if (at >= from && at < to)
                    list.Add(new PendingDelay(at, Math.Max(120, Normal(rng, 240, 20)), "Scheduled maintenance", true));
            }
        }

        // Unplanned: breakdown (~20%/truck/day, 1-3 h) and tyre (~3%/truck/day, ~1 h).
        for (var day = from.Date; day < to; day = day.AddDays(1))
        {
            if (rng.NextDouble() < 0.20)
            {
                var at = day.AddMinutes(rng.NextDouble() * 24 * 60);
                if (at >= from && at < to)
                    list.Add(new PendingDelay(at, 60 + rng.NextDouble() * 120, "Breakdown", false));
            }
            if (rng.NextDouble() < 0.03)
            {
                var at = day.AddMinutes(rng.NextDouble() * 24 * 60);
                if (at >= from && at < to)
                    list.Add(new PendingDelay(at, Math.Max(20, Normal(rng, 60, 10)), "Tyre", false));
            }
        }

        list.Sort((a, b) => a.RequestedStart.CompareTo(b.RequestedStart));
        return list;
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

/// <summary>Drives one truck forward through time, producing cycles and delays in order,
/// with no overlaps. Used by both history mode (bounded) and live mode (unbounded, one
/// event at a time).</summary>
class TruckTimeline
{
    public Truck Truck { get; }
    public DateTime Cursor { get; private set; }

    private readonly Random _rng;
    private readonly Simulator _sim;
    private readonly LoaderSpikeSchedule _spikes;
    private readonly List<PendingDelay> _pending = new();
    private DateTime _plannedUntil;

    public TruckTimeline(Truck truck, DateTime start, Random rng, Simulator sim, LoaderSpikeSchedule spikes)
    {
        Truck = truck;
        Cursor = start;
        _rng = rng;
        _sim = sim;
        _spikes = spikes;
        _plannedUntil = start;
    }

    public void EnsurePlanned(DateTime upTo)
    {
        if (_plannedUntil >= upTo) return;
        _pending.AddRange(DelayPlanner.Plan(_rng, _plannedUntil, upTo));
        _pending.Sort((a, b) => a.RequestedStart.CompareTo(b.RequestedStart));
        _plannedUntil = upTo;
    }

    /// <summary>Produces the next cycle or delay and advances the cursor past it.
    /// If horizon is given (history mode), plans no further than needed; otherwise
    /// (live mode) plans a rolling 2-day window ahead of the cursor.</summary>
    public object Next(DateTime? horizon = null)
    {
        var lookAhead = Cursor.AddDays(2);
        EnsurePlanned(horizon.HasValue && horizon.Value < lookAhead ? horizon.Value : lookAhead);
        _spikes.EnsurePlanned(horizon.HasValue && horizon.Value < lookAhead ? horizon.Value : lookAhead);

        if (_pending.Count > 0 && _pending[0].RequestedStart <= Cursor)
        {
            var d = _pending[0];
            _pending.RemoveAt(0);
            var start = Cursor;
            var end = start.AddMinutes(d.DurationMin);
            Cursor = end;
            return new DelayRow(Truck.Id, start, end, d.Reason, d.IsPlanned);
        }

        var row = _sim.BuildCycle(Truck, Cursor, _spikes);
        Cursor = Cursor.AddMinutes(Simulator.TotalMin(row));
        return row;
    }
}

class Simulator
{
    private readonly Fleet _fleet;
    private readonly Random _rng;

    public Simulator(Fleet fleet, Random rng)
    {
        _fleet = fleet;
        _rng = rng;
    }

    public static double TotalMin(CycleRow r) =>
        r.LoadMin + r.HaulMin + r.DumpMin + r.ReturnMin + r.QueueMin;

    public CycleRow BuildCycle(Truck truck, DateTime start, LoaderSpikeSchedule spikes)
    {
        var loader = _fleet.Loaders[_rng.Next(_fleet.Loaders.Count)];
        var route = PickRoute();

        var loadMin = Math.Max(2.0, Normal(3.8, 0.6));
        var dumpMin = Math.Max(0.6, Normal(1.2, 0.3));

        // Simple speed model: steeper grade = slower. Loaded trucks are slower than empty ones.
        var loadedSpeedKmh = 32 - 1.5 * route.GradePercent;
        var emptySpeedKmh = 42 - 0.8 * route.GradePercent;

        var haulMin = route.DistanceKm / loadedSpeedKmh * 60 * route.SlowFactor * Math.Max(0.8, Normal(1, 0.06));
        var returnMin = route.DistanceKm / emptySpeedKmh * 60 * Math.Max(0.8, Normal(1, 0.06));

        var queueMin = QueueMinutes(start, loader.Id, spikes);

        // Payload as a share of truck capacity. Planted problem #1: T07 runs ~80% instead of ~97%.
        var share = truck.Underloaded ? Normal(0.80, 0.04) : Normal(0.97, 0.03);
        var payload = Math.Clamp(share, 0.5, 1.05) * truck.CapacityTonnes;

        // Illustrative fuel burn in litres per minute: driving loaded > driving empty > idling/queueing.
        var fuel = haulMin * 4.2 + returnMin * 3.0 + (loadMin + dumpMin + queueMin) * 1.2;

        return new CycleRow(
            truck.Id, loader.Id, route.Id, start,
            R(loadMin), R(haulMin), R(dumpMin), R(returnMin), R(queueMin),
            Math.Round(payload, 1), Math.Round(fuel, 1));
    }

    // Planted problem #3: fleet-wide queue rise at shift change (06:00 and 18:00 hour).
    // Planted problem #4: a loader-specific queue spike (see LoaderSpikeSchedule).
    private double QueueMinutes(DateTime start, int loaderId, LoaderSpikeSchedule spikes)
    {
        var atShiftChange = start.Hour == 6 || start.Hour == 18;
        var inSpike = spikes.IsInSpike(loaderId, start);
        var mean = (atShiftChange || inSpike) ? 5.0 : 0.8;
        var q = -mean * Math.Log(1.0 - _rng.NextDouble()); // exponential: usually small, sometimes long
        return Math.Min(q, 25);
    }

    private Route PickRoute()
    {
        var r = _rng.NextDouble();
        return r < 0.50 ? _fleet.Routes[0]
             : r < 0.80 ? _fleet.Routes[1]
             : _fleet.Routes[2];
    }

    // Bell-curve random number (Box-Muller transform).
    private double Normal(double mean, double sd)
    {
        var u1 = 1.0 - _rng.NextDouble();
        var u2 = _rng.NextDouble();
        return mean + sd * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    private static double R(double v) => Math.Round(v, 2);
}
