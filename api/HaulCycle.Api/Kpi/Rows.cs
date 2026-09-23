// Plain data-in rows for the KPI calculators. No DB types here (no SqlConnection,
// no Dapper attributes beyond what plain property mapping needs) so xUnit can build
// these directly with in-memory data and the calculators stay unit-testable.

namespace HaulCycle.Api.Kpi;

/// <summary>One row from vw_CycleDetail.</summary>
public sealed record CycleRow(
    DateTime StartTime,
    string ShiftName,
    DateOnly ShiftDate,
    string TruckName,
    string LoaderName,
    string RouteName,
    string DestinationName,
    string Material,
    decimal LoadMin,
    decimal HaulMin,
    decimal DumpMin,
    decimal ReturnMin,
    decimal QueueMin,
    decimal TotalCycleMin,
    decimal PayloadTonnes,
    decimal CapacityTonnes,
    decimal PayloadPercentOfCapacity,
    decimal FuelLitres);

/// <summary>One row from dbo.Delays (joined to Trucks for the name).</summary>
public sealed record DelayRow(
    string TruckName,
    DateTime StartTime,
    DateTime EndTime,
    string Reason,
    bool IsPlanned);

/// <summary>One row from vw_ScheduleDetail. Route/Loader/Destination/plan fields are
/// null when the truck was unavailable for the shift.</summary>
public sealed record ScheduleRow(
    DateOnly ShiftDate,
    string ShiftName,
    string TruckName,
    string? RouteName,
    string? LoaderName,
    string? DestinationName,
    string? Material,
    decimal? PlannedCycles,
    decimal? PlannedTonnes,
    string? UnavailableReason);
