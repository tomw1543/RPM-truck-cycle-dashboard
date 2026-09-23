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
