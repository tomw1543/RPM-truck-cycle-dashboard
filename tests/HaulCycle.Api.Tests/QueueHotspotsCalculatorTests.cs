using HaulCycle.Api.Kpi;

namespace HaulCycle.Api.Tests;

public class QueueHotspotsCalculatorTests
{
    [Fact]
    public void BenchmarkByLoader_Percentile25PerLoader()
    {
        var all = new[]
        {
            TestData.Cycle(loader: "L1", queueMin: 1m),
            TestData.Cycle(loader: "L1", queueMin: 2m),
            TestData.Cycle(loader: "L1", queueMin: 3m),
            TestData.Cycle(loader: "L1", queueMin: 4m),
            TestData.Cycle(loader: "L2", queueMin: 10m),
        };

        var benchmark = QueueHotspotsCalculator.BenchmarkByLoader(all);

        // L1 sorted 1,2,3,4 -> rank 0.25*3=0.75 -> 1 + (2-1)*0.75 = 1.75
        Assert.Equal(1.75m, benchmark["L1"]);
        Assert.Equal(10m, benchmark["L2"]);
    }

    [Fact]
    public void Calculate_ExcessAboveBenchmark_BucketsByLoaderAndHour()
    {
        var allCycles = new[]
        {
            TestData.Cycle(loader: "L1", queueMin: 1m),
            TestData.Cycle(loader: "L1", queueMin: 1m),
        };
        var window = new[]
        {
            TestData.Cycle(loader: "L1", start: new DateTime(2026, 9, 1, 6, 5, 0), queueMin: 6m),
            TestData.Cycle(loader: "L1", start: new DateTime(2026, 9, 1, 6, 40, 0), queueMin: 4m),
            TestData.Cycle(loader: "L1", start: new DateTime(2026, 9, 1, 10, 0, 0), queueMin: 1m), // at benchmark, no excess
        };

        var result = QueueHotspotsCalculator.Calculate(allCycles, window);

        var hour6 = Assert.Single(result.Top);
        Assert.Equal("L1", hour6.LoaderName);
        Assert.Equal(new DateTime(2026, 9, 1, 6, 0, 0), hour6.DateHour);
        Assert.Equal(8m, hour6.ExcessMin); // (6-1) + (4-1)
        Assert.Equal(2, hour6.Cycles);

        Assert.Contains(result.Profile, p => p.LoaderName == "L1" && p.HourOfDay == 6 && p.ExcessMin == 8m);
        Assert.DoesNotContain(result.Profile, p => p.HourOfDay == 10 && p.ExcessMin > 0m);
    }

    [Fact]
    public void Calculate_LoaderWithNoBenchmark_Skipped()
    {
        var window = new[] { TestData.Cycle(loader: "Unknown", queueMin: 5m) };
        var result = QueueHotspotsCalculator.Calculate([], window);
        Assert.Empty(result.Top);
        Assert.Empty(result.Profile);
    }
}
