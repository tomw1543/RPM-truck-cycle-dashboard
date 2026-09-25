using Dapper;
using HaulCycle.Api.Kpi;

namespace HaulCycle.Api.Data;

public sealed record FleetCounts(int Trucks, int Loaders, int Routes, int Destinations);

/// <summary>One row from dbo.Trucks, for endpoints that need the full truck roster (including
/// trucks with zero cycles in the requested window) rather than only the trucks that show up
/// in vw_CycleDetail.</summary>
public sealed record TruckInfo(string Name, decimal CapacityTonnes);

public sealed record MetaInfo(DateTime? AsOf, DateOnly? FirstDate, DateOnly? LastDate, FleetCounts Counts);

/// <summary>
/// Dapper queries against vw_CycleDetail / vw_ScheduleDetail / Delays. SQL does the
/// filtering and grouping; the KPI classes in Kpi/ do the maths over the rows returned
/// here, so those stay testable with plain in-memory data.
/// </summary>
public sealed class HaulCycleQueries(IDbConnectionFactory connectionFactory)
{
    public async Task<MetaInfo> GetMetaAsync(CancellationToken ct = default)
    {
        using var conn = connectionFactory.CreateConnection();

        const string coverageSql = """
            SELECT
                MAX(DATEADD(SECOND, CAST(ROUND(TotalCycleMin * 60, 0) AS INT), StartTime)) AS AsOf,
                MIN(CAST(StartTime AS DATE)) AS FirstDate,
                MAX(CAST(StartTime AS DATE)) AS LastDate
            FROM dbo.vw_CycleDetail;
            """;

        const string countsSql = """
            SELECT
                (SELECT COUNT(*) FROM dbo.Trucks) AS Trucks,
                (SELECT COUNT(*) FROM dbo.Loaders) AS Loaders,
                (SELECT COUNT(*) FROM dbo.Routes) AS Routes,
                (SELECT COUNT(*) FROM dbo.Destinations) AS Destinations;
            """;

        var coverageCommand = new CommandDefinition(coverageSql, cancellationToken: ct);
        var countsCommand = new CommandDefinition(countsSql, cancellationToken: ct);

        var coverage = await conn.QuerySingleAsync<(DateTime? AsOf, DateTime? FirstDate, DateTime? LastDate)>(coverageCommand);
        var counts = await conn.QuerySingleAsync<FleetCounts>(countsCommand);

        return new MetaInfo(
            coverage.AsOf,
            coverage.FirstDate.HasValue ? DateOnly.FromDateTime(coverage.FirstDate.Value) : null,
            coverage.LastDate.HasValue ? DateOnly.FromDateTime(coverage.LastDate.Value) : null,
            counts);
    }

    public async Task<FleetCounts> GetFleetCountsAsync(CancellationToken ct = default)
    {
        using var conn = connectionFactory.CreateConnection();
        const string sql = """
            SELECT
                (SELECT COUNT(*) FROM dbo.Trucks) AS Trucks,
                (SELECT COUNT(*) FROM dbo.Loaders) AS Loaders,
                (SELECT COUNT(*) FROM dbo.Routes) AS Routes,
                (SELECT COUNT(*) FROM dbo.Destinations) AS Destinations;
            """;
        return await conn.QuerySingleAsync<FleetCounts>(new CommandDefinition(sql, cancellationToken: ct));
    }

    /// <summary>Every truck on the roster, ordered by name - includes trucks with no cycles in
    /// any window, so callers building one row per truck don't silently drop a quiet truck.</summary>
    public async Task<IReadOnlyList<TruckInfo>> GetTrucksAsync(CancellationToken ct = default)
    {
        using var conn = connectionFactory.CreateConnection();
        const string sql = "SELECT Name, CapacityTonnes FROM dbo.Trucks ORDER BY Name;";
        var rows = await conn.QueryAsync<TruckInfo>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<CycleRow>> GetCyclesAsync(DateTime from, DateTime to, string? shift, CancellationToken ct = default)
    {
        using var conn = connectionFactory.CreateConnection();
        const string sql = """
            SELECT
                StartTime, ShiftName, ShiftDate, TruckName, LoaderName, RouteName, DestinationName, Material,
                LoadMin, HaulMin, DumpMin, ReturnMin, QueueMin, TotalCycleMin, PayloadTonnes, CapacityTonnes,
                PayloadPercentOfCapacity, FuelLitres
            FROM dbo.vw_CycleDetail
            WHERE StartTime >= @From AND StartTime < @To
              AND (@Shift IS NULL OR ShiftName = @Shift);
            """;
        var rows = await conn.QueryAsync<CycleRow>(new CommandDefinition(sql, new { From = from, To = to, Shift = shift }, cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>Cycles selected by ShiftDate (not StartTime), for plan-vs-actual only. Plan vs
    /// actual is a shift-grained measure: it must pull the numerator (actual tonnes, from cycles)
    /// and the denominator (planned tonnes, from schedules) from exactly the same set of whole
    /// shifts - the ones whose ShiftDate falls in [fromDate, toDate]. Filtering cycles by raw
    /// StartTime instead (as GetCyclesAsync does, correctly, for cycle-grained KPIs) clips the
    /// window's first and last shifts inconsistently with the shift-filtered schedules, especially
    /// for the Night shift that crosses midnight - that mismatch was the plan-vs-actual defect.</summary>
    public async Task<IReadOnlyList<CycleRow>> GetCyclesForShiftWindowAsync(DateOnly fromDate, DateOnly toDate, string? shift, CancellationToken ct = default)
    {
        using var conn = connectionFactory.CreateConnection();
        const string sql = """
            SELECT
                StartTime, ShiftName, ShiftDate, TruckName, LoaderName, RouteName, DestinationName, Material,
                LoadMin, HaulMin, DumpMin, ReturnMin, QueueMin, TotalCycleMin, PayloadTonnes, CapacityTonnes,
                PayloadPercentOfCapacity, FuelLitres
            FROM dbo.vw_CycleDetail
            WHERE ShiftDate >= @FromDate AND ShiftDate <= @ToDate
              AND (@Shift IS NULL OR ShiftName = @Shift);
            """;
        var rows = await conn.QueryAsync<CycleRow>(new CommandDefinition(
            sql,
            new { FromDate = fromDate.ToDateTime(TimeOnly.MinValue), ToDate = toDate.ToDateTime(TimeOnly.MinValue), Shift = shift },
            cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<DelayRow>> GetDelaysAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        using var conn = connectionFactory.CreateConnection();
        const string sql = """
            SELECT t.Name AS TruckName, d.StartTime, d.EndTime, d.Reason, d.IsPlanned
            FROM dbo.Delays d
            JOIN dbo.Trucks t ON t.TruckId = d.TruckId
            WHERE d.StartTime < @To AND d.EndTime > @From;
            """;
        var rows = await conn.QueryAsync<DelayRow>(new CommandDefinition(sql, new { From = from, To = to }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<ScheduleRow>> GetSchedulesAsync(DateOnly fromDate, DateOnly toDate, string? shift, CancellationToken ct = default)
    {
        using var conn = connectionFactory.CreateConnection();
        const string sql = """
            SELECT ShiftDate, ShiftName, TruckName, RouteName, LoaderName, DestinationName, Material,
                   PlannedCycles, PlannedTonnes, UnavailableReason
            FROM dbo.vw_ScheduleDetail
            WHERE ShiftDate >= @FromDate AND ShiftDate <= @ToDate
              AND (@Shift IS NULL OR ShiftName = @Shift);
            """;
        var rows = await conn.QueryAsync<ScheduleRow>(new CommandDefinition(
            sql,
            new { FromDate = fromDate.ToDateTime(TimeOnly.MinValue), ToDate = toDate.ToDateTime(TimeOnly.MinValue), Shift = shift },
            cancellationToken: ct));
        return rows.AsList();
    }
}
