using HaulCycle.Api.Kpi;

namespace HaulCycle.Api.Tests;

public class OptimiserSummaryCalculatorTests
{
    // Day shift on 2026-09-01 ends 2026-09-01 18:00; Night the same date ends 2026-09-02 06:00.
    private static readonly DateTime AsOf = new(2026, 9, 5, 0, 0, 0);

    [Fact]
    public void OptimisedShift_GainsComputedAgainstOriginal()
    {
        var schedules = new[] { TestData.Schedule("T01") };
        var plans = new[]
        {
            TestData.OptimisedPlan("Original", totalTonnesMean: 1000m, totalTonnesMin: 950m, totalTonnesMax: 1050m,
                queueHoursMean: 10m, fuelLitresMean: 5000m, truckHoursMean: 144m, trucksStoodDownMean: 0m),
            TestData.OptimisedPlan("MoreOutput", totalTonnesMean: 1200m, totalTonnesMin: 1150m, totalTonnesMax: 1250m,
                queueHoursMean: 8m, fuelLitresMean: 4800m, truckHoursMean: 144m, trucksStoodDownMean: 0m),
            TestData.OptimisedPlan("Leaner", totalTonnesMean: 1000m, totalTonnesMin: 950m, totalTonnesMax: 1050m,
                queueHoursMean: 9m, fuelLitresMean: 4900m, truckHoursMean: 132m, trucksStoodDownMean: 1m),
        };

        var result = OptimiserSummaryCalculator.Calculate(schedules, plans, AsOf);

        Assert.Equal(1, result.ShiftsOptimised);
        Assert.Equal(0, result.ShiftsNotOptimised);

        Assert.Equal(200m, result.MoreOutput.TonnesGainedMean); // 1200 - 1000
        // Conservative: candidate.Min - original.Max = 1150 - 1050
        Assert.Equal(100m, result.MoreOutput.TonnesGainedMin);
        // Optimistic: candidate.Max - original.Min = 1250 - 950
        Assert.Equal(300m, result.MoreOutput.TonnesGainedMax);
        Assert.Equal(2m, result.MoreOutput.QueueHoursSaved); // 10 - 8
        Assert.Equal(200m, result.MoreOutput.FuelLitresSaved); // 5000 - 4800
        Assert.Equal(0m, result.MoreOutput.TruckHoursSaved);
        Assert.Equal(0m, result.MoreOutput.TrucksStoodDown);

        Assert.Equal(0m, result.Leaner.TonnesGainedMean);
        Assert.Equal(12m, result.Leaner.TruckHoursSaved); // 144 - 132
        Assert.Equal(1m, result.Leaner.TrucksStoodDown);

        var shift = Assert.Single(result.Shifts);
        Assert.True(shift.HasResults);
        Assert.Equal(200m, shift.MoreOutputTonnesGainedMean);
        Assert.Equal(12m, shift.LeanerTruckHoursSavedMean);
    }

    [Fact]
    public void CompleteShiftWithoutResults_CountsAsNotOptimisedAndListedWithoutGains()
    {
        var schedules = new[] { TestData.Schedule("T01") };
        var plans = Array.Empty<OptimisedPlanRow>();

        var result = OptimiserSummaryCalculator.Calculate(schedules, plans, AsOf);

        Assert.Equal(0, result.ShiftsOptimised);
        Assert.Equal(1, result.ShiftsNotOptimised);

        var shift = Assert.Single(result.Shifts);
        Assert.False(shift.HasResults);
        Assert.Null(shift.MoreOutputTonnesGainedMean);
        Assert.Null(shift.LeanerTruckHoursSavedMean);

        Assert.Equal(0m, result.MoreOutput.TonnesGainedMean);
        Assert.Equal(0m, result.Leaner.TruckHoursSaved);
    }

    [Fact]
    public void IncompleteShift_NeverCountedAsNotOptimised()
    {
        // asOf right at the boundary of the Day shift's end - the Night shift starting the same
        // day hasn't finished yet, so it must never inflate ShiftsNotOptimised.
        var asOf = new DateTime(2026, 9, 1, 20, 0, 0);
        var schedules = new[] { TestData.Schedule("T01", shiftName: "Night") };

        var result = OptimiserSummaryCalculator.Calculate(schedules, Array.Empty<OptimisedPlanRow>(), asOf);

        Assert.Equal(0, result.ShiftsOptimised);
        Assert.Equal(0, result.ShiftsNotOptimised);
        Assert.Empty(result.Shifts);
    }

    [Fact]
    public void ShiftStartingBeforeTheData_NeverCountedAsNotOptimised()
    {
        // The data begins at midnight on 2 Sep, so the 1 Sep Night shift (18:00 to 06:00) is only
        // half covered: --optimise skips it, and rerunning it never would, so it must not be
        // listed as "not optimised yet". The 2 Sep Day shift is fully covered.
        var asOf = new DateTime(2026, 9, 3, 0, 0, 0);
        var dataStart = new DateTime(2026, 9, 2, 0, 0, 0);
        var schedules = new[]
        {
            TestData.Schedule("T01", shiftDate: new DateOnly(2026, 9, 1), shiftName: "Night"),
            TestData.Schedule("T01", shiftDate: new DateOnly(2026, 9, 2), shiftName: "Day"),
        };

        var result = OptimiserSummaryCalculator.Calculate(schedules, Array.Empty<OptimisedPlanRow>(), asOf, dataStart);

        Assert.Equal(1, result.ShiftsNotOptimised);
        var shift = Assert.Single(result.Shifts);
        Assert.Equal(new DateOnly(2026, 9, 2), shift.ShiftDate);
    }

    [Fact]
    public void MultipleShifts_WholePeriodGainsAreSummedPerShift()
    {
        var schedules = new[] { TestData.Schedule("T01", shiftDate: new DateOnly(2026, 9, 1)), TestData.Schedule("T01", shiftDate: new DateOnly(2026, 9, 2)) };
        var plans = new[]
        {
            TestData.OptimisedPlan("Original", shiftDate: new DateOnly(2026, 9, 1), totalTonnesMean: 1000m, totalTonnesMin: 1000m, totalTonnesMax: 1000m),
            TestData.OptimisedPlan("MoreOutput", shiftDate: new DateOnly(2026, 9, 1), totalTonnesMean: 1100m, totalTonnesMin: 1100m, totalTonnesMax: 1100m),
            TestData.OptimisedPlan("Leaner", shiftDate: new DateOnly(2026, 9, 1), totalTonnesMean: 1000m, totalTonnesMin: 1000m, totalTonnesMax: 1000m),

            TestData.OptimisedPlan("Original", shiftDate: new DateOnly(2026, 9, 2), totalTonnesMean: 2000m, totalTonnesMin: 2000m, totalTonnesMax: 2000m),
            TestData.OptimisedPlan("MoreOutput", shiftDate: new DateOnly(2026, 9, 2), totalTonnesMean: 2050m, totalTonnesMin: 2050m, totalTonnesMax: 2050m),
            TestData.OptimisedPlan("Leaner", shiftDate: new DateOnly(2026, 9, 2), totalTonnesMean: 2000m, totalTonnesMin: 2000m, totalTonnesMax: 2000m),
        };

        var result = OptimiserSummaryCalculator.Calculate(schedules, plans, AsOf);

        Assert.Equal(2, result.ShiftsOptimised);
        Assert.Equal(150m, result.MoreOutput.TonnesGainedMean); // 100 + 50
        Assert.Equal(2, result.Shifts.Count);
    }
}
