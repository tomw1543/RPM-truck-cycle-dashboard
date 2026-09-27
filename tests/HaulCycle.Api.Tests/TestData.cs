using HaulCycle.Api.Kpi;

namespace HaulCycle.Api.Tests;

/// <summary>Small factories for building KPI-calculator rows with sensible defaults,
/// so each test only spells out the fields it cares about.</summary>
internal static class TestData
{
    public static CycleRow Cycle(
        DateTime? start = null,
        string truck = "T01",
        string loader = "L1",
        string route = "L1-ROM pad",
        string destination = "ROM pad",
        string material = "Ore",
        decimal loadMin = 3.8m,
        decimal haulMin = 10m,
        decimal dumpMin = 1.2m,
        decimal returnMin = 4m,
        decimal queueMin = 0.8m,
        decimal? totalCycleMin = null,
        decimal payloadTonnes = 213m,
        decimal capacityTonnes = 220m,
        DateOnly? shiftDate = null,
        string shiftName = "Day")
    {
        var total = totalCycleMin ?? loadMin + haulMin + dumpMin + returnMin + queueMin;
        return new CycleRow(
            start ?? new DateTime(2026, 9, 1, 8, 0, 0),
            shiftName,
            shiftDate ?? new DateOnly(2026, 9, 1),
            truck, loader, route, destination, material,
            loadMin, haulMin, dumpMin, returnMin, queueMin, total,
            payloadTonnes, capacityTonnes,
            Math.Round(100m * payloadTonnes / capacityTonnes, 1),
            180m);
    }

    public static DelayRow Delay(string truck, DateTime start, DateTime end, string reason = "Breakdown", bool isPlanned = false) =>
        new(truck, start, end, reason, isPlanned);

    public static OptimisedPlanRow OptimisedPlan(
        string planType,
        DateOnly? shiftDate = null,
        string shiftName = "Day",
        int seedCount = 5,
        decimal totalTonnesMean = 1000m, decimal totalTonnesMin = 950m, decimal totalTonnesMax = 1050m,
        decimal crusherTonnesMean = 400m, decimal crusherTonnesMin = 380m, decimal crusherTonnesMax = 420m,
        decimal romTonnesMean = 300m, decimal romTonnesMin = 285m, decimal romTonnesMax = 315m,
        decimal wasteTonnesMean = 300m, decimal wasteTonnesMin = 285m, decimal wasteTonnesMax = 315m,
        decimal cyclesMean = 200m, decimal cyclesMin = 190m, decimal cyclesMax = 210m,
        decimal queueHoursMean = 10m, decimal queueHoursMin = 8m, decimal queueHoursMax = 12m,
        decimal fuelLitresMean = 5000m, decimal fuelLitresMin = 4800m, decimal fuelLitresMax = 5200m,
        decimal truckHoursMean = 144m, decimal truckHoursMin = 144m, decimal truckHoursMax = 144m,
        decimal trucksStoodDownMean = 0m, decimal trucksStoodDownMin = 0m, decimal trucksStoodDownMax = 0m) =>
        new(
            shiftDate ?? new DateOnly(2026, 9, 1), shiftName, planType, seedCount,
            totalTonnesMean, totalTonnesMin, totalTonnesMax,
            crusherTonnesMean, crusherTonnesMin, crusherTonnesMax,
            romTonnesMean, romTonnesMin, romTonnesMax,
            wasteTonnesMean, wasteTonnesMin, wasteTonnesMax,
            cyclesMean, cyclesMin, cyclesMax,
            queueHoursMean, queueHoursMin, queueHoursMax,
            fuelLitresMean, fuelLitresMin, fuelLitresMax,
            truckHoursMean, truckHoursMin, truckHoursMax,
            trucksStoodDownMean, trucksStoodDownMin, trucksStoodDownMax);

    public static OptimisedAssignmentRow OptimisedAssignment(
        string planType,
        string truckName,
        string? routeName,
        string? loaderName,
        string? destinationName,
        bool isStoodDown = false,
        bool isUnavailable = false) =>
        new(planType, truckName, routeName, loaderName, destinationName, isStoodDown, isUnavailable);

    public static OptimisedLoaderStatRow OptimisedLoaderStat(
        string planType,
        string loaderName,
        int trucks,
        decimal avgQueueMin,
        decimal utilisation,
        decimal loadingMin = 300m,
        decimal matchFactor = 1m) =>
        new(planType, loaderName, trucks, avgQueueMin, loadingMin, utilisation, matchFactor);

    public static RouteReferenceRow RouteReference(
        string routeName,
        string loaderName,
        string destinationName,
        decimal distanceKm,
        decimal bookCycleMin = 20m) =>
        new(routeName, loaderName, destinationName, distanceKm, bookCycleMin);

    public static ScheduleRow Schedule(
        string truck,
        string? destination = "ROM pad",
        decimal? plannedTonnes = 1000m,
        decimal? plannedCycles = 20m,
        string? unavailableReason = null,
        DateOnly? shiftDate = null,
        string shiftName = "Day") =>
        new(
            shiftDate ?? new DateOnly(2026, 9, 1),
            shiftName,
            truck,
            unavailableReason is null ? "L1-" + destination : null,
            unavailableReason is null ? "L1" : null,
            unavailableReason is null ? destination : null,
            unavailableReason is null ? "Ore" : null,
            unavailableReason is null ? plannedCycles : null,
            unavailableReason is null ? plannedTonnes : null,
            unavailableReason);
}
