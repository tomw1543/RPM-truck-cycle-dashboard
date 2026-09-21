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

    // Every truck's full delay timeline is planned up front (deterministic given the
    // window), so the scheduler can see it before any cycle is simulated.
    var truckDelayLists = new Dictionary<int, List<PendingDelay>>();
    foreach (var truck in fleet.Trucks)
        truckDelayLists[truck.Id] = DelayPlanner.Plan(rng, from, to);

    // Build every shift's schedule (all trucks) in chronological order before running
    // any truck's timeline, since cycles must follow the shift's assigned route.
    var scheduleRows = new List<ScheduleRow>();
    var scheduleLookup = new Dictionary<(int TruckId, DateOnly ShiftDate, string ShiftName), ScheduleRow>();
    for (var shiftStart = FloorToShiftBoundary(from); shiftStart < to; shiftStart = shiftStart.AddHours(12))
    {
        var shiftEnd = shiftStart.AddHours(12);
        var shiftName = shiftStart.Hour == 6 ? "Day" : "Night";
        var shiftDate = DateOnly.FromDateTime(shiftStart.Date);

        var rows = Scheduler.BuildShiftSchedules(
            fleet, shiftStart, shiftEnd, shiftName, shiftDate,
            truckId => truckDelayLists[truckId].Select(d => (d.RequestedStart, d.RequestedStart.AddMinutes(d.DurationMin), d.Reason, d.IsPlanned)),
            rng);

        scheduleRows.AddRange(rows);
        foreach (var r in rows)
            scheduleLookup[(r.TruckId, r.ShiftDate, r.ShiftName)] = r;
    }

    ScheduleRow? Lookup(int truckId, DateOnly shiftDate, string shiftName) =>
        scheduleLookup.TryGetValue((truckId, shiftDate, shiftName), out var row) ? row : null;

    var cycles = new List<CycleRow>();
    var delays = new List<DelayRow>();

    foreach (var truck in fleet.Trucks)
    {
        var cursor0 = from.AddMinutes(rng.Next(0, 15)); // stagger truck start times
        var timeline = new TruckTimeline(truck, cursor0, rng, sim, spikes, fleet, Lookup, truckDelayLists[truck.Id]);

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
    await BulkInsertSchedulesAsync(conn, scheduleRows);

    Console.WriteLine($"Done. Inserted {cycles.Count:N0} cycles, {delays.Count:N0} delays, {scheduleRows.Count:N0} schedules. Crusher guard fired {Scheduler.GuardFireCount} time(s).");
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

    // Start the sim clock at the EARLIEST per-truck latest-end time, not the latest.
    // Starting at the max meant most trucks' next event was already "due" the instant
    // the clock started, firing a burst of catch-up inserts instead of pacing normally.
    var simClockStart = initialCursor.Values.DefaultIfEmpty(now).Min();
    var spikes = new LoaderSpikeSchedule(fleet.Loaders, rng, simClockStart);

    // Shared, mutable schedule store for live mode: schedules are created just-in-time,
    // per truck, the first time a truck's timeline needs one (see TruckTimeline.Next).
    var scheduleLookup = new Dictionary<(int TruckId, DateOnly ShiftDate, string ShiftName), ScheduleRow>();
    await LoadExistingSchedulesAsync(conn, scheduleLookup);

    async void OnScheduleCreated(ScheduleRow row)
    {
        scheduleLookup[(row.TruckId, row.ShiftDate, row.ShiftName)] = row;
        await InsertOneScheduleAsync(conn, row);
        Console.WriteLine($"  schedule: {row.ShiftDate:yyyy-MM-dd} {row.ShiftName} truck {row.TruckId,2} -> " +
            (row.UnavailableReason is { } reason ? $"unavailable ({reason})" : $"route {row.RouteId} ({row.PlannedCycles:0.0} planned cycles)"));
    }

    ScheduleRow GetOrCreateSchedule(int truckId, DateOnly shiftDate, string shiftName, DateTime shiftStart, DateTime shiftEnd, List<PendingDelay> knownDelays, DelayRow? lastDelay)
    {
        var key = (truckId, shiftDate, shiftName);
        if (scheduleLookup.TryGetValue(key, out var existing))
            return existing;

        var truck = fleet.Trucks.First(t => t.Id == truckId);
        var row = Scheduler.BuildSingleTruckSchedule(fleet, truck, shiftStart, shiftEnd, shiftName, shiftDate, knownDelays, lastDelay, rng);
        OnScheduleCreated(row);
        return row;
    }

    var timelines = fleet.Trucks.ToDictionary(
        t => t.Id,
        t => new TruckTimeline(t, initialCursor[t.Id], rng, sim, spikes, fleet, GetOrCreateSchedule));

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
                    var ev = timelines[truck.Id].Next()!; // live mode never passes a horizon, so this never returns null
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
// Shift helpers
// ---------------------------------------------------------------------------

DateTime FloorToShiftBoundary(DateTime t)
{
    var six = t.Date.AddHours(6);
    var eighteen = t.Date.AddHours(18);
    if (t >= eighteen) return eighteen;
    if (t >= six) return six;
    return t.Date.AddDays(-1).AddHours(18);
}

// ---------------------------------------------------------------------------
// Schema
// ---------------------------------------------------------------------------

async Task<bool> TablesExistAsync(SqlConnection conn)
{
    await using var cmd = new SqlCommand(
        "SELECT COUNT(*) FROM sys.tables WHERE name IN ('Trucks','Loaders','Destinations','Routes','Cycles','Delays','Schedules');", conn);
    var count = (int)(await cmd.ExecuteScalarAsync())!;
    return count == 7;
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
    // Cycles/Delays/Schedules are not referenced by any other table, so TRUNCATE works
    // (and resets identities). Reference tables must be cleared in FK-dependency order.
    await ExecAsync(conn, "TRUNCATE TABLE dbo.Delays;");
    await ExecAsync(conn, "TRUNCATE TABLE dbo.Cycles;");
    await ExecAsync(conn, "TRUNCATE TABLE dbo.Schedules;");
    await ExecAsync(conn, "DELETE FROM dbo.Routes;");
    await ExecAsync(conn, "DELETE FROM dbo.Destinations;");
    await ExecAsync(conn, "DELETE FROM dbo.Loaders;");
    await ExecAsync(conn, "DELETE FROM dbo.Trucks;");

    foreach (var t in fleet.Trucks)
    {
        await using var cmd = new SqlCommand(
            "INSERT INTO dbo.Trucks (TruckId, Name, CapacityTonnes, EmptyMassTonnes) VALUES (@id, @name, @cap, @empty);", conn);
        cmd.Parameters.AddWithValue("@id", t.Id);
        cmd.Parameters.AddWithValue("@name", t.Name);
        cmd.Parameters.AddWithValue("@cap", (decimal)t.CapacityTonnes);
        cmd.Parameters.AddWithValue("@empty", (decimal)t.EmptyMassTonnes);
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

    foreach (var d in fleet.Destinations)
    {
        await using var cmd = new SqlCommand(
            "INSERT INTO dbo.Destinations (DestinationId, Name, Material) VALUES (@id, @name, @material);", conn);
        cmd.Parameters.AddWithValue("@id", d.Id);
        cmd.Parameters.AddWithValue("@name", d.Name);
        cmd.Parameters.AddWithValue("@material", d.Material);
        await cmd.ExecuteNonQueryAsync();
    }

    foreach (var r in fleet.Routes)
    {
        await using var cmd = new SqlCommand(
            "INSERT INTO dbo.Routes (RouteId, Name, LoaderId, DestinationId, DistanceKm, GradePercent) VALUES (@id, @name, @loader, @dest, @dist, @grade);", conn);
        cmd.Parameters.AddWithValue("@id", r.Id);
        cmd.Parameters.AddWithValue("@name", r.Name);
        cmd.Parameters.AddWithValue("@loader", r.LoaderId);
        cmd.Parameters.AddWithValue("@dest", r.DestinationId);
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

async Task BulkInsertSchedulesAsync(SqlConnection conn, List<ScheduleRow> rows)
{
    var table = new DataTable();
    table.Columns.Add("ShiftDate", typeof(DateTime));
    table.Columns.Add("ShiftName", typeof(string));
    table.Columns.Add("TruckId", typeof(int));
    table.Columns.Add("RouteId", typeof(int));
    table.Columns.Add("LoaderId", typeof(int));
    table.Columns.Add("PlannedCycles", typeof(decimal));
    table.Columns.Add("PlannedTonnes", typeof(decimal));
    table.Columns.Add("UnavailableReason", typeof(string));

    foreach (var r in rows)
    {
        table.Rows.Add(
            r.ShiftDate.ToDateTime(TimeOnly.MinValue), r.ShiftName, r.TruckId,
            (object?)r.RouteId ?? DBNull.Value,
            (object?)r.LoaderId ?? DBNull.Value,
            r.PlannedCycles.HasValue ? (decimal)r.PlannedCycles.Value : DBNull.Value,
            r.PlannedTonnes.HasValue ? (decimal)r.PlannedTonnes.Value : DBNull.Value,
            (object?)r.UnavailableReason ?? DBNull.Value);
    }

    using var bulk = new SqlBulkCopy(conn)
    {
        DestinationTableName = "dbo.Schedules",
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

async Task InsertOneScheduleAsync(SqlConnection conn, ScheduleRow r)
{
    await using var cmd = new SqlCommand(@"
        INSERT INTO dbo.Schedules (ShiftDate, ShiftName, TruckId, RouteId, LoaderId, PlannedCycles, PlannedTonnes, UnavailableReason)
        VALUES (@shiftDate, @shiftName, @truck, @route, @loader, @cycles, @tonnes, @reason);", conn);
    cmd.Parameters.AddWithValue("@shiftDate", r.ShiftDate.ToDateTime(TimeOnly.MinValue));
    cmd.Parameters.AddWithValue("@shiftName", r.ShiftName);
    cmd.Parameters.AddWithValue("@truck", r.TruckId);
    cmd.Parameters.AddWithValue("@route", (object?)r.RouteId ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@loader", (object?)r.LoaderId ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@cycles", r.PlannedCycles.HasValue ? (decimal)r.PlannedCycles.Value : DBNull.Value);
    cmd.Parameters.AddWithValue("@tonnes", r.PlannedTonnes.HasValue ? (decimal)r.PlannedTonnes.Value : DBNull.Value);
    cmd.Parameters.AddWithValue("@reason", (object?)r.UnavailableReason ?? DBNull.Value);
    await cmd.ExecuteNonQueryAsync();
}

async Task LoadExistingSchedulesAsync(SqlConnection conn, Dictionary<(int, DateOnly, string), ScheduleRow> into)
{
    // Only pull in schedules for shifts that could still be "current" (today onward),
    // so a long-lived live session doesn't load the entire history table.
    await using var cmd = new SqlCommand(@"
        SELECT ShiftDate, ShiftName, TruckId, RouteId, LoaderId, PlannedCycles, PlannedTonnes, UnavailableReason
        FROM dbo.Schedules
        WHERE ShiftDate >= CAST(DATEADD(DAY, -1, SYSDATETIME()) AS DATE);", conn);
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var row = new ScheduleRow(
            DateOnly.FromDateTime(reader.GetDateTime(0)),
            reader.GetString(1),
            reader.GetInt32(2),
            reader.IsDBNull(3) ? null : reader.GetInt32(3),
            reader.IsDBNull(4) ? null : reader.GetInt32(4),
            reader.IsDBNull(5) ? null : (double)reader.GetDecimal(5),
            reader.IsDBNull(6) ? null : (double)reader.GetDecimal(6),
            reader.IsDBNull(7) ? null : reader.GetString(7));
        into[(row.TruckId, row.ShiftDate, row.ShiftName)] = row;
    }
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

record Truck(int Id, string Name, double CapacityTonnes, double EmptyMassTonnes, bool Underloaded);
record Loader(int Id, string Name);
record Destination(int Id, string Name, string Material);
record Route(int Id, string Name, int LoaderId, int DestinationId, double DistanceKm, double GradePercent);

record CycleRow(
    int TruckId, int LoaderId, int RouteId, DateTime StartTime,
    double LoadMin, double HaulMin, double DumpMin, double ReturnMin, double QueueMin,
    double PayloadTonnes, double FuelLitres);

record DelayRow(int TruckId, DateTime StartTime, DateTime EndTime, string Reason, bool IsPlanned);

/// <summary>One truck's assignment for one shift: a route/loader with planned cycles and
/// tonnes, or a reason it's unavailable (RouteId/LoaderId/plans all null in that case).</summary>
record ScheduleRow(
    DateOnly ShiftDate, string ShiftName, int TruckId,
    int? RouteId, int? LoaderId, double? PlannedCycles, double? PlannedTonnes, string? UnavailableReason);

record PendingEvent(object Event, DateTime End);

/// <summary>A planned or unplanned delay before it has been "placed" on a truck's timeline.
/// RequestedStart is when it should ideally start; if a cycle is in progress at that time,
/// TruckTimeline starts it as soon as that cycle ends instead.</summary>
record PendingDelay(DateTime RequestedStart, double DurationMin, string Reason, bool IsPlanned);

class Fleet
{
    public const int RomPadDestinationId = 1;
    public const int WasteDumpDestinationId = 2;
    public const int CrusherDestinationId = 3;

    public List<Truck> Trucks { get; } = new();
    public List<Loader> Loaders { get; } = new();
    public List<Destination> Destinations { get; } = new();
    public List<Route> Routes { get; } = new();

    public Route RouteById(int id) => Routes.First(r => r.Id == id);
    public Loader LoaderById(int id) => Loaders.First(l => l.Id == id);
    public Destination DestinationById(int id) => Destinations.First(d => d.Id == id);

    public static Fleet Create()
    {
        var f = new Fleet();

        // 12 trucks. T07 is the "planted" underloaded truck. The dashboard should discover this.
        // (The flag lives only in this program - it is NOT stored in the database.)
        // EmptyMassTonnes is uniform across the fleet (illustrative, not modelled per-truck).
        for (var i = 1; i <= 12; i++)
            f.Trucks.Add(new Truck(i, $"T{i:00}", 220, EmptyMassTonnes: 165, Underloaded: i == 7));

        for (var i = 1; i <= 3; i++)
            f.Loaders.Add(new Loader(i, $"L{i}"));

        f.Destinations.Add(new Destination(RomPadDestinationId, "ROM pad", "Ore"));
        f.Destinations.Add(new Destination(WasteDumpDestinationId, "Waste dump", "Waste"));
        f.Destinations.Add(new Destination(CrusherDestinationId, "Crusher", "Ore"));

        // Every loader x destination pair is a route. Home pairings (below) keep the
        // original numbers and are each loader's shortest route by construction; the other
        // six pairings are hardcoded, roughly 1-2.5 km longer than the destination's home
        // route (a bit more where that alone wouldn't keep the home route shortest).
        f.Routes.Add(new Route(1, "L1 to ROM pad",     LoaderId: 1, DestinationId: RomPadDestinationId,     DistanceKm: 3.2, GradePercent: 6.0)); // home
        f.Routes.Add(new Route(2, "L1 to Waste dump",  LoaderId: 1, DestinationId: WasteDumpDestinationId,  DistanceKm: 6.0, GradePercent: 8.5));
        f.Routes.Add(new Route(3, "L1 to Crusher",     LoaderId: 1, DestinationId: CrusherDestinationId,    DistanceKm: 3.5, GradePercent: 6.0));

        f.Routes.Add(new Route(4, "L2 to ROM pad",     LoaderId: 2, DestinationId: RomPadDestinationId,     DistanceKm: 5.2, GradePercent: 5.5));
        f.Routes.Add(new Route(5, "L2 to Waste dump",  LoaderId: 2, DestinationId: WasteDumpDestinationId,  DistanceKm: 4.8, GradePercent: 8.0)); // home
        f.Routes.Add(new Route(6, "L2 to Crusher",     LoaderId: 2, DestinationId: CrusherDestinationId,    DistanceKm: 4.9, GradePercent: 7.0));

        f.Routes.Add(new Route(7, "L3 to ROM pad",     LoaderId: 3, DestinationId: RomPadDestinationId,     DistanceKm: 4.5, GradePercent: 6.5));
        f.Routes.Add(new Route(8, "L3 to Waste dump",  LoaderId: 3, DestinationId: WasteDumpDestinationId,  DistanceKm: 5.9, GradePercent: 8.0));
        f.Routes.Add(new Route(9, "L3 to Crusher",     LoaderId: 3, DestinationId: CrusherDestinationId,    DistanceKm: 2.1, GradePercent: 5.0)); // home

        return f;
    }
}

/// <summary>The speed model shared by the simulator (with noise/slow-ramp/underload) and
/// the scheduler's book rates (without them).</summary>
static class SpeedModel
{
    public static double LoadedSpeedKmh(double gradePercent) => 32 - 1.5 * gradePercent;
    public static double EmptySpeedKmh(double gradePercent) => 42 - 0.8 * gradePercent;
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

        // Unplanned: breakdown (~20%/truck/day, 1-3 h), tyre (~3%/truck/day, ~1 h),
        // and major breakdown (~2%/truck/day, 1-3 days - long enough to span shifts).
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
            if (rng.NextDouble() < 0.02)
            {
                var at = day.AddMinutes(rng.NextDouble() * 24 * 60);
                if (at >= from && at < to)
                    list.Add(new PendingDelay(at, (1 + rng.NextDouble() * 2) * 24 * 60, "Major breakdown", false));
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

/// <summary>Builds a shift's Schedules rows from book rates and each truck's known delays.
/// History mode calls BuildShiftSchedules once per shift for the whole fleet (so the crusher
/// guard can see everyone's assignment). Live mode calls BuildSingleTruckSchedule per truck,
/// just-in-time, from that truck's own known-delay snapshot; it does not apply the crusher
/// guard, since schedules there are created independently as each truck's timeline reaches
/// the shift (documented simplification - see README).</summary>
static class Scheduler
{
    public const double FullPayloadTonnes = 220;
    public const double BookLoadMin = 3.8;
    public const double BookDumpMin = 1.2;
    public const double BookQueueMin = 0.8;

    /// <summary>How many times ApplyCrusherGuard actually had to move a truck (history mode only; reset per run).</summary>
    public static int GuardFireCount;

    /// <summary>Book cycle time at full payload, no slow-ramp, no underload noise.</summary>
    public static double BookCycleMinutes(Route route)
    {
        var loadedSpeed = SpeedModel.LoadedSpeedKmh(route.GradePercent);
        var emptySpeed = SpeedModel.EmptySpeedKmh(route.GradePercent);
        var haul = route.DistanceKm / loadedSpeed * 60;
        var ret = route.DistanceKm / emptySpeed * 60;
        return BookLoadMin + haul + BookDumpMin + ret + BookQueueMin;
    }

    public static List<ScheduleRow> BuildShiftSchedules(
        Fleet fleet, DateTime shiftStart, DateTime shiftEnd, string shiftName, DateOnly shiftDate,
        Func<int, IEnumerable<(DateTime Start, DateTime End, string Reason, bool IsPlanned)>> delaysForTruck,
        Random rng)
    {
        var rows = new List<ScheduleRow>();

        foreach (var truck in fleet.Trucks)
        {
            var delays = delaysForTruck(truck.Id).ToList();
            rows.Add(BuildOne(fleet, truck, shiftStart, shiftEnd, shiftName, shiftDate, delays, rng));
        }

        ApplyCrusherGuard(fleet, rows, rng);
        return rows;
    }

    public static ScheduleRow BuildSingleTruckSchedule(
        Fleet fleet, Truck truck, DateTime shiftStart, DateTime shiftEnd, string shiftName, DateOnly shiftDate,
        List<PendingDelay> knownUpcomingDelays, DelayRow? lastDelay, Random rng)
    {
        var delays = knownUpcomingDelays
            .Select(d => (d.RequestedStart, d.RequestedStart.AddMinutes(d.DurationMin), d.Reason, d.IsPlanned))
            .ToList();
        if (lastDelay != null)
            delays.Add((lastDelay.StartTime, lastDelay.EndTime, lastDelay.Reason, lastDelay.IsPlanned));

        return BuildOne(fleet, truck, shiftStart, shiftEnd, shiftName, shiftDate, delays, rng);
    }

    private static ScheduleRow BuildOne(
        Fleet fleet, Truck truck, DateTime shiftStart, DateTime shiftEnd, string shiftName, DateOnly shiftDate,
        List<(DateTime Start, DateTime End, string Reason, bool IsPlanned)> delays, Random rng)
    {
        var activeAtShiftStart = delays
            .Where(d => d.Start <= shiftStart && shiftStart < d.End)
            .ToList();

        string? unavailableReason = null;
        foreach (var d in activeAtShiftStart)
        {
            if (d.Reason == "Major breakdown") { unavailableReason = d.Reason; break; }
            if (d.End >= shiftEnd) { unavailableReason = d.Reason; break; } // covers the whole shift
        }

        if (unavailableReason != null)
            return new ScheduleRow(shiftDate, shiftName, truck.Id, null, null, null, null, unavailableReason);

        var knownMinutesOut = delays
            .Where(d => d.IsPlanned)
            .Sum(d => OverlapMinutes(d.Start, d.End, shiftStart, shiftEnd));
        var availableMin = Math.Max(0, 720 - knownMinutesOut);

        var route = fleet.Routes[rng.Next(fleet.Routes.Count)];
        var bookMin = BookCycleMinutes(route);
        var plannedCycles = bookMin > 0 ? availableMin / bookMin : 0;

        return new ScheduleRow(
            shiftDate, shiftName, truck.Id, route.Id, route.LoaderId,
            Math.Round(plannedCycles, 2), Math.Round(plannedCycles * FullPayloadTonnes, 1), null);
    }

    private static void ApplyCrusherGuard(Fleet fleet, List<ScheduleRow> rows, Random rng)
    {
        var anyCrusher = rows.Any(r => r.RouteId.HasValue && fleet.RouteById(r.RouteId.Value).DestinationId == Fleet.CrusherDestinationId);
        if (anyCrusher) return;

        GuardFireCount++;
        var eligible = rows.Where(r => r.RouteId.HasValue).ToList();
        if (eligible.Count == 0) return;

        var pick = eligible[rng.Next(eligible.Count)];
        var crusherRoutes = fleet.Routes.Where(r => r.DestinationId == Fleet.CrusherDestinationId).ToList();
        var newRoute = crusherRoutes[rng.Next(crusherRoutes.Count)];

        var oldBookMin = BookCycleMinutes(fleet.RouteById(pick.RouteId!.Value));
        var availableMin = (pick.PlannedCycles ?? 0) * oldBookMin;
        var newBookMin = BookCycleMinutes(newRoute);
        var plannedCycles = newBookMin > 0 ? availableMin / newBookMin : 0;

        var idx = rows.IndexOf(pick);
        rows[idx] = pick with
        {
            RouteId = newRoute.Id,
            LoaderId = newRoute.LoaderId,
            PlannedCycles = Math.Round(plannedCycles, 2),
            PlannedTonnes = Math.Round(plannedCycles * FullPayloadTonnes, 1)
        };
    }

    private static double OverlapMinutes(DateTime aStart, DateTime aEnd, DateTime bStart, DateTime bEnd)
    {
        var start = aStart > bStart ? aStart : bStart;
        var end = aEnd < bEnd ? aEnd : bEnd;
        return end > start ? (end - start).TotalMinutes : 0;
    }
}

/// <summary>Drives one truck forward through time, producing cycles and delays in order,
/// with no overlaps. Cycles use the truck's Schedules assignment for the shift they start
/// in; if the schedule says the truck is unavailable, the truck stays idle (no cycles, no
/// synthetic rows) until the next shift boundary. Used by both history mode (a fully
/// pre-planned delay list and schedule lookup) and live mode (a rolling delay window and a
/// just-in-time schedule lookup).</summary>
class TruckTimeline
{
    public Truck Truck { get; }
    public DateTime Cursor { get; private set; }

    private readonly Random _rng;
    private readonly Simulator _sim;
    private readonly LoaderSpikeSchedule _spikes;
    private readonly Fleet _fleet;
    private readonly List<PendingDelay> _pending = new();
    private readonly bool _fullyPlanned;
    private DateTime _plannedUntil;
    private DelayRow? _lastDelay;

    // History: schedules for the whole window are already known.
    private readonly Func<int, DateOnly, string, ScheduleRow?>? _scheduleLookup;
    // Live: schedules are created just-in-time from this truck's known-delay snapshot.
    private readonly Func<int, DateOnly, string, DateTime, DateTime, List<PendingDelay>, DelayRow?, ScheduleRow>? _scheduleFactory;

    public TruckTimeline(Truck truck, DateTime start, Random rng, Simulator sim, LoaderSpikeSchedule spikes, Fleet fleet,
        Func<int, DateOnly, string, ScheduleRow?> scheduleLookup, List<PendingDelay> preplannedDelays)
    {
        Truck = truck;
        Cursor = start;
        _rng = rng;
        _sim = sim;
        _spikes = spikes;
        _fleet = fleet;
        _plannedUntil = start;
        _scheduleLookup = scheduleLookup;

        _pending.AddRange(preplannedDelays);
        _pending.Sort((a, b) => a.RequestedStart.CompareTo(b.RequestedStart));
        _fullyPlanned = true;
    }

    public TruckTimeline(Truck truck, DateTime start, Random rng, Simulator sim, LoaderSpikeSchedule spikes, Fleet fleet,
        Func<int, DateOnly, string, DateTime, DateTime, List<PendingDelay>, DelayRow?, ScheduleRow> scheduleFactory)
    {
        Truck = truck;
        Cursor = start;
        _rng = rng;
        _sim = sim;
        _spikes = spikes;
        _fleet = fleet;
        _plannedUntil = start;
        _scheduleFactory = scheduleFactory;
    }

    public void EnsurePlanned(DateTime upTo)
    {
        if (_fullyPlanned) return;
        if (_plannedUntil >= upTo) return;
        _pending.AddRange(DelayPlanner.Plan(_rng, _plannedUntil, upTo));
        _pending.Sort((a, b) => a.RequestedStart.CompareTo(b.RequestedStart));
        _plannedUntil = upTo;
    }

    /// <summary>Produces the next cycle or delay and advances the cursor past it.
    /// If horizon is given (history mode), plans no further than needed; otherwise
    /// (live mode) plans a rolling 2-day window ahead of the cursor. Returns null only
    /// when the cursor has been advanced past horizon while skipping unavailable shifts.</summary>
    public object? Next(DateTime? horizon = null)
    {
        while (true)
        {
            var lookAhead = Cursor.AddDays(2);
            var planTo = horizon.HasValue && horizon.Value < lookAhead ? horizon.Value : lookAhead;
            EnsurePlanned(planTo);
            _spikes.EnsurePlanned(planTo);

            if (_pending.Count > 0 && _pending[0].RequestedStart <= Cursor)
            {
                var d = _pending[0];
                _pending.RemoveAt(0);
                var start = Cursor;
                var end = start.AddMinutes(d.DurationMin);
                Cursor = end;
                var row = new DelayRow(Truck.Id, start, end, d.Reason, d.IsPlanned);
                _lastDelay = row;
                return row;
            }

            var (shiftDate, shiftName, shiftStart, shiftEnd) = ShiftOf(Cursor);
            var schedule = _scheduleLookup != null
                ? _scheduleLookup(Truck.Id, shiftDate, shiftName)
                : _scheduleFactory!(Truck.Id, shiftDate, shiftName, shiftStart, shiftEnd, _pending, _lastDelay);

            if (schedule == null || schedule.RouteId == null)
            {
                // Unavailable for (the rest of) this shift: idle, no row, until the next shift.
                Cursor = shiftEnd;
                if (horizon.HasValue && Cursor >= horizon.Value) return null;
                continue;
            }

            var route = _fleet.RouteById(schedule.RouteId.Value);
            var row2 = _sim.BuildCycle(Truck, Cursor, route, _spikes);
            Cursor = Cursor.AddMinutes(Simulator.TotalMin(row2));
            return row2;
        }
    }

    private static (DateOnly ShiftDate, string ShiftName, DateTime ShiftStart, DateTime ShiftEnd) ShiftOf(DateTime t)
    {
        var six = t.Date.AddHours(6);
        var eighteen = t.Date.AddHours(18);
        DateTime start, end;
        if (t >= eighteen) { start = eighteen; end = t.Date.AddDays(1).AddHours(6); }
        else if (t >= six) { start = six; end = eighteen; }
        else { start = t.Date.AddDays(-1).AddHours(18); end = six; }
        var shiftName = start.Hour == 6 ? "Day" : "Night";
        return (DateOnly.FromDateTime(start.Date), shiftName, start, end);
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

    public CycleRow BuildCycle(Truck truck, DateTime start, Route route, LoaderSpikeSchedule spikes)
    {
        var loader = _fleet.LoaderById(route.LoaderId);

        // Payload is drawn BEFORE load/haul time, since both depend on it.
        // Planted problem #1: T07 runs ~80% of capacity instead of ~97%.
        var share = truck.Underloaded ? Normal(0.80, 0.04) : Normal(0.97, 0.03);
        var payload = Math.Clamp(share, 0.5, 1.05) * truck.CapacityTonnes;

        // Load time scales with payload share: a ~97% load averages ~3.8 min, ~80% ~3.1 min.
        var baseLoad = Math.Max(2.0, Normal(3.8, 0.6));
        var loadMin = baseLoad * (payload / (0.97 * truck.CapacityTonnes));

        var dumpMin = Math.Max(0.6, Normal(1.2, 0.3));

        // Simple speed model: steeper grade = slower. Loaded trucks are slower than empty ones.
        var loadedSpeedKmh = SpeedModel.LoadedSpeedKmh(route.GradePercent);
        var emptySpeedKmh = SpeedModel.EmptySpeedKmh(route.GradePercent);

        // Loaded haul time (and haul fuel) scale with the gross weight ratio.
        var grossWeightRatio = (truck.EmptyMassTonnes + payload) / (truck.EmptyMassTonnes + truck.CapacityTonnes);

        // Planted problem #2: every route into the waste dump runs ~15% slower than the
        // speed model predicts (a slow ramp, code-only - not stored per-route in the DB).
        var slowFactor = route.DestinationId == Fleet.WasteDumpDestinationId ? 1.15 : 1.00;

        var haulMin = route.DistanceKm / loadedSpeedKmh * 60 * slowFactor * grossWeightRatio * Math.Max(0.8, Normal(1, 0.06));
        var returnMin = route.DistanceKm / emptySpeedKmh * 60 * Math.Max(0.8, Normal(1, 0.06));

        var queueMin = QueueMinutes(start, loader.Id, spikes);

        // Illustrative fuel burn in litres per minute: driving loaded > driving empty > idling/queueing.
        // Haul fuel scales with the same gross weight ratio as haul time.
        var fuel = haulMin * 4.2 * grossWeightRatio + returnMin * 3.0 + (loadMin + dumpMin + queueMin) * 1.2;

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

    // Bell-curve random number (Box-Muller transform).
    private double Normal(double mean, double sd)
    {
        var u1 = 1.0 - _rng.NextDouble();
        var u2 = _rng.NextDouble();
        return mean + sd * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    private static double R(double v) => Math.Round(v, 2);
}
