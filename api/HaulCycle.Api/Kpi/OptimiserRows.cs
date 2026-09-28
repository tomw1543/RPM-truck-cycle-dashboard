// Plain data-in rows for the optimiser KPI calculators. No DB types here, same convention as
// Rows.cs, so xUnit can build these directly with in-memory data.

namespace HaulCycle.Api.Kpi;

/// <summary>One row from vw_OptimisedPlanDetail: one plan (Original/MoreOutput/Leaner) for one
/// shift, with every outcome's mean and min/max over the plan's SeedCount replays. TruckHours
/// and TrucksStoodDown are plan-determined, not random, so their Mean/Min/Max are always equal
/// for a given plan - see the schema comment in db/schema.sql.</summary>
public sealed record OptimisedPlanRow(
    DateOnly ShiftDate,
    string ShiftName,
    string PlanType,
    int SeedCount,
    decimal TotalTonnesMean, decimal TotalTonnesMin, decimal TotalTonnesMax,
    decimal CrusherTonnesMean, decimal CrusherTonnesMin, decimal CrusherTonnesMax,
    decimal RomTonnesMean, decimal RomTonnesMin, decimal RomTonnesMax,
    decimal WasteTonnesMean, decimal WasteTonnesMin, decimal WasteTonnesMax,
    decimal CyclesMean, decimal CyclesMin, decimal CyclesMax,
    decimal QueueHoursMean, decimal QueueHoursMin, decimal QueueHoursMax,
    decimal FuelLitresMean, decimal FuelLitresMin, decimal FuelLitresMax,
    decimal TruckHoursMean, decimal TruckHoursMin, decimal TruckHoursMax,
    decimal TrucksStoodDownMean, decimal TrucksStoodDownMin, decimal TrucksStoodDownMax);

/// <summary>One row from vw_OptimisedAssignmentDetail: one truck's assignment under one plan.
/// RouteName/LoaderName/DestinationName are null for an unavailable or stood-down truck. No
/// MoveReason field here any more - the stored column is dead (the generator now writes NULL to
/// it), and MoveReasonCalculator builds the sentence at read time from this row plus the plan's
/// loader stats and tonnes, so wording changes never need a --optimise rerun.</summary>
public sealed record OptimisedAssignmentRow(
    string PlanType,
    string TruckName,
    string? RouteName,
    string? LoaderName,
    string? DestinationName,
    bool IsStoodDown,
    bool IsUnavailable);

/// <summary>One row from vw_OptimisedLoaderStatDetail: one loader's stats under one plan,
/// averaged over the plan's replays.</summary>
public sealed record OptimisedLoaderStatRow(
    string PlanType,
    string LoaderName,
    int Trucks,
    decimal AvgQueueMin,
    decimal LoadingMin,
    decimal Utilisation,
    decimal MatchFactor);

/// <summary>One route's loader/destination/distance, for MoveReasonCalculator's route-length
/// clause - kept as a plain Kpi-layer row (rather than reusing Data.RouteInfo) so the calculator
/// stays free of DB types, same convention as RouteTargetRow in Rows.cs.</summary>
public sealed record RouteReferenceRow(
    string RouteName,
    string LoaderName,
    string DestinationName,
    decimal DistanceKm,
    decimal TargetCycleMin);
