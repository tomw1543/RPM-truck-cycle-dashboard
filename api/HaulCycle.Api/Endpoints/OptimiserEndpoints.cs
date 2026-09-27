using HaulCycle.Api.Data;
using HaulCycle.Api.Kpi;

namespace HaulCycle.Api.Endpoints;

public sealed record OptimiserPlanGainData(
    decimal TonnesGainedMean, decimal TonnesGainedMin, decimal TonnesGainedMax,
    decimal QueueHoursSaved, decimal FuelLitresSaved, decimal TruckHoursSaved, decimal TrucksStoodDown);

public sealed record OptimiserShiftHeadlineData(
    DateOnly ShiftDate, string ShiftName, bool HasResults,
    decimal? MoreOutputTonnesGainedMean, decimal? LeanerTruckHoursSavedMean);

public sealed record OptimiserSummaryData(
    int ShiftsOptimised, int ShiftsNotOptimised,
    OptimiserPlanGainData MoreOutput, OptimiserPlanGainData Leaner,
    IReadOnlyList<OptimiserShiftHeadlineData> Shifts);

public sealed record RangeData(decimal Mean, decimal Min, decimal Max);

public sealed record OptimiserOutcomeData(
    RangeData TotalTonnes, RangeData CrusherTonnes, RangeData RomTonnes, RangeData WasteTonnes,
    RangeData Cycles, RangeData QueueHours, RangeData FuelLitres, RangeData TruckHours, RangeData TrucksStoodDown);

public sealed record OptimiserAssignmentData(
    string TruckName, string? RouteName, string? LoaderName, string? DestinationName,
    bool IsStoodDown, bool IsUnavailable, string? MoveReason);

public sealed record OptimiserLoaderStatData(
    string LoaderName, int Trucks, decimal AvgQueueMin, decimal LoadingMin, decimal Utilisation, decimal MatchFactor);

public sealed record OptimiserPlanData(
    string PlanType, int SeedCount, string? PlanSummary, OptimiserOutcomeData Outcome,
    IReadOnlyList<OptimiserAssignmentData> Assignments, IReadOnlyList<OptimiserLoaderStatData> LoaderStats);

public sealed record ActualShiftOutcomeData(
    decimal CrusherTonnes, decimal RomTonnes, decimal WasteTonnes, decimal TotalTonnes,
    int Cycles, decimal QueueHours, decimal FuelLitres);

public sealed record OptimiserShiftDetailData(
    DateOnly ShiftDate, string ShiftName,
    OptimiserPlanData Original, OptimiserPlanData MoreOutput, OptimiserPlanData Leaner,
    ActualShiftOutcomeData Actual);

/// <summary>/api/optimiser/summary and /api/optimiser/shifts/{shiftDate}/{shiftName} - read-only
/// views over the tables OptimiserEngine (data-generator, --optimise) writes. Nothing here scores
/// or searches a plan; it only reads back what --optimise already computed and replayed.</summary>
public static class OptimiserEndpoints
{
    public static void MapOptimiserEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/optimiser").CacheOutput("DataEndpoints");

        group.MapGet("/summary", async Task<IResult> (
            DateOnly? from,
            DateOnly? to,
            string? shift,
            HaulCycleQueries queries,
            CancellationToken ct) =>
        {
            var meta = await queries.GetMetaAsync(ct);
            if (meta.AsOf is null)
                return Results.Ok(new Envelope<OptimiserSummaryData>(null, from, to, EmptySummary()));

            if (!RequestWindow.TryResolve(from, to, shift, meta.AsOf.Value, out var fromDt, out var toDt, out var fromDate, out var toDate, out var problem))
                return problem!;

            var schedules = await queries.GetSchedulesAsync(fromDate, toDate, shift, ct);
            var plans = await queries.GetOptimisedPlansAsync(fromDate, toDate, shift, ct);

            // FirstDate is the first cycle's date; history loads start at midnight, so its 00:00 is
            // when the data begins (the same bound --optimise uses to skip the partial first shift).
            var dataStart = meta.FirstDate?.ToDateTime(TimeOnly.MinValue);
            var result = OptimiserSummaryCalculator.Calculate(schedules, plans, meta.AsOf.Value, dataStart);

            var data = new OptimiserSummaryData(
                result.ShiftsOptimised,
                result.ShiftsNotOptimised,
                MapGain(result.MoreOutput),
                MapGain(result.Leaner),
                result.Shifts.Select(s => new OptimiserShiftHeadlineData(
                    s.ShiftDate, s.ShiftName, s.HasResults, s.MoreOutputTonnesGainedMean, s.LeanerTruckHoursSavedMean)).ToList());

            return Results.Ok(new Envelope<OptimiserSummaryData>(meta.AsOf, fromDate, toDate, data));
        })
        .WithName("GetOptimiserSummary")
        .WithSummary("Whole-window headline (tonnes gained, hours/fuel saved, trucks stood down) for MoreOutput and Leaner vs Original, plus a per-shift list for the shift picker.");

        group.MapGet("/shifts/{shiftDate}/{shiftName}", async Task<IResult> (
            DateOnly shiftDate,
            string shiftName,
            HaulCycleQueries queries,
            CancellationToken ct) =>
        {
            var plans = await queries.GetOptimisedPlansForShiftAsync(shiftDate, shiftName, ct);
            if (plans.Count == 0)
            {
                return TypedResults.Problem(
                    title: "Shift not optimised",
                    detail: $"No --optimise results for {shiftDate:yyyy-MM-dd} {shiftName}. Either the shift doesn't exist, isn't complete yet, or --optimise hasn't been run since it was generated.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            var assignments = await queries.GetOptimisedAssignmentsForShiftAsync(shiftDate, shiftName, ct);
            var loaderStats = await queries.GetOptimisedLoaderStatsForShiftAsync(shiftDate, shiftName, ct);
            var actualCycles = await queries.GetCyclesForShiftWindowAsync(shiftDate, shiftDate, shiftName, ct);
            var actual = ActualShiftOutcomeCalculator.Calculate(actualCycles);
            var meta = await queries.GetMetaAsync(ct);
            var routes = (await queries.GetRoutesAsync(ct))
                .Select(r => new RouteReferenceRow(r.RouteName, r.LoaderName, r.DestinationName, r.DistanceKm, r.BookCycleMin))
                .ToList();

            var originalPlan = plans.First(p => p.PlanType == "Original");
            var originalAssignments = assignments.Where(a => a.PlanType == "Original").ToList();
            var originalLoaderStats = loaderStats.Where(l => l.PlanType == "Original").ToList();

            OptimiserPlanData BuildPlan(string planType)
            {
                var plan = plans.First(p => p.PlanType == planType);
                var planAssignmentRows = assignments.Where(a => a.PlanType == planType).ToList();
                var planLoaderStatRows = loaderStats.Where(l => l.PlanType == planType).ToList();

                // Original never has a reason (nothing to compare it against) or a summary
                // sentence (there's no "candidate vs Original" to summarise for Original itself).
                var reasonResult = planType == "Original"
                    ? null
                    : MoveReasonCalculator.Calculate(originalAssignments, planAssignmentRows, originalLoaderStats, planLoaderStatRows, originalPlan, plan, routes);
                var moveReasons = reasonResult?.ReasonsByTruck ?? new Dictionary<string, string>();
                var planSummary = reasonResult?.Summary;

                var planAssignments = planAssignmentRows
                    .OrderBy(a => a.TruckName, StringComparer.Ordinal)
                    .Select(a => new OptimiserAssignmentData(
                        a.TruckName, a.RouteName, a.LoaderName, a.DestinationName, a.IsStoodDown, a.IsUnavailable,
                        moveReasons.GetValueOrDefault(a.TruckName)))
                    .ToList();
                var planLoaderStats = planLoaderStatRows
                    .OrderBy(l => l.LoaderName, StringComparer.Ordinal)
                    .Select(l => new OptimiserLoaderStatData(l.LoaderName, l.Trucks, l.AvgQueueMin, l.LoadingMin, l.Utilisation, l.MatchFactor))
                    .ToList();

                return new OptimiserPlanData(plan.PlanType, plan.SeedCount, planSummary, MapOutcome(plan), planAssignments, planLoaderStats);
            }

            var data = new OptimiserShiftDetailData(
                shiftDate, shiftName,
                BuildPlan("Original"), BuildPlan("MoreOutput"), BuildPlan("Leaner"),
                new ActualShiftOutcomeData(actual.CrusherTonnes, actual.RomTonnes, actual.WasteTonnes, actual.TotalTonnes, actual.Cycles, actual.QueueHours, actual.FuelLitres));

            return Results.Ok(new Envelope<OptimiserShiftDetailData>(meta.AsOf, shiftDate, shiftDate, data));
        })
        .WithName("GetOptimiserShift")
        .WithSummary("All three plans (Original, MoreOutput, Leaner) for one shift - assignments with move reasons, loader stats, outcome ranges - plus the shift's actual recorded outcome for the faithfulness comparison.");
    }

    private static OptimiserPlanGainData MapGain(OptimiserSummaryCalculator.PlanGain g) =>
        new(g.TonnesGainedMean, g.TonnesGainedMin, g.TonnesGainedMax, g.QueueHoursSaved, g.FuelLitresSaved, g.TruckHoursSaved, g.TrucksStoodDown);

    private static OptimiserOutcomeData MapOutcome(OptimisedPlanRow p) => new(
        new RangeData(p.TotalTonnesMean, p.TotalTonnesMin, p.TotalTonnesMax),
        new RangeData(p.CrusherTonnesMean, p.CrusherTonnesMin, p.CrusherTonnesMax),
        new RangeData(p.RomTonnesMean, p.RomTonnesMin, p.RomTonnesMax),
        new RangeData(p.WasteTonnesMean, p.WasteTonnesMin, p.WasteTonnesMax),
        new RangeData(p.CyclesMean, p.CyclesMin, p.CyclesMax),
        new RangeData(p.QueueHoursMean, p.QueueHoursMin, p.QueueHoursMax),
        new RangeData(p.FuelLitresMean, p.FuelLitresMin, p.FuelLitresMax),
        new RangeData(p.TruckHoursMean, p.TruckHoursMin, p.TruckHoursMax),
        new RangeData(p.TrucksStoodDownMean, p.TrucksStoodDownMin, p.TrucksStoodDownMax));

    private static OptimiserSummaryData EmptySummary() =>
        new(0, 0, new OptimiserPlanGainData(0, 0, 0, 0, 0, 0, 0), new OptimiserPlanGainData(0, 0, 0, 0, 0, 0, 0), []);
}
