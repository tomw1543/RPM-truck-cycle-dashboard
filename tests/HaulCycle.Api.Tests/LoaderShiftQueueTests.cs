using HaulCycle.Api.Endpoints;
using HaulCycle.Api.Kpi;

namespace HaulCycle.Api.Tests;

public class LoaderShiftQueueTests
{
    private static readonly string[] LoaderNames = ["L1", "L2", "L3"];

    [Fact]
    public void BuildLoaderShiftQueueData_GroupsArrivalsByLoader()
    {
        var cycles = new[]
        {
            TestData.Cycle(truck: "T01", loader: "L1", start: new DateTime(2026, 9, 1, 8, 0, 0), queueMin: 1.2m),
            TestData.Cycle(truck: "T02", loader: "L1", start: new DateTime(2026, 9, 1, 8, 30, 0), queueMin: 2.5m),
            TestData.Cycle(truck: "T03", loader: "L2", start: new DateTime(2026, 9, 1, 9, 0, 0), queueMin: 0.8m),
        };

        var result = LoaderEndpoints.BuildLoaderShiftQueueData(cycles, [], LoaderNames);

        Assert.Equal(3, result.Loaders.Count);

        var l1 = result.Loaders.Single(l => l.LoaderName == "L1");
        Assert.Equal(2, l1.Arrivals.Count);
        Assert.Equal("T01", l1.Arrivals[0].TruckName);
        Assert.Equal(1.2m, l1.Arrivals[0].QueueMin);
        Assert.Equal("T02", l1.Arrivals[1].TruckName);
        Assert.Equal(2.5m, l1.Arrivals[1].QueueMin);

        var l2 = result.Loaders.Single(l => l.LoaderName == "L2");
        Assert.Single(l2.Arrivals);
        Assert.Equal("T03", l2.Arrivals[0].TruckName);
    }

    [Fact]
    public void BuildLoaderShiftQueueData_EmptyLoaderHasEmptyArrivals()
    {
        var cycles = new[]
        {
            TestData.Cycle(truck: "T01", loader: "L1"),
        };

        var result = LoaderEndpoints.BuildLoaderShiftQueueData(cycles, [], LoaderNames);

        var l3 = result.Loaders.Single(l => l.LoaderName == "L3");
        Assert.Empty(l3.Arrivals);
        Assert.Empty(l3.Delays);
    }

    [Fact]
    public void BuildLoaderShiftQueueData_IncludesDelayWindows()
    {
        var delays = new[]
        {
            TestData.LoaderDelay("L1", new DateTime(2026, 9, 1, 6, 0, 0), new DateTime(2026, 9, 1, 6, 15, 0), rateFactor: 0.00m),
            TestData.LoaderDelay("L2", new DateTime(2026, 9, 1, 10, 0, 0), new DateTime(2026, 9, 1, 10, 45, 0), rateFactor: 0.50m),
        };

        var result = LoaderEndpoints.BuildLoaderShiftQueueData([], delays, LoaderNames);

        var l1 = result.Loaders.Single(l => l.LoaderName == "L1");
        Assert.Single(l1.Delays);
        Assert.Equal(0.00m, l1.Delays[0].RateFactor);

        var l2 = result.Loaders.Single(l => l.LoaderName == "L2");
        Assert.Single(l2.Delays);
        Assert.Equal(0.50m, l2.Delays[0].RateFactor);
    }

    [Fact]
    public void BuildLoaderShiftQueueData_ArrivalsOrderedByStartTime()
    {
        // Cycles arrive out of order; expect them sorted ascending by StartTime.
        var cycles = new[]
        {
            TestData.Cycle(truck: "T02", loader: "L1", start: new DateTime(2026, 9, 1, 10, 0, 0)),
            TestData.Cycle(truck: "T01", loader: "L1", start: new DateTime(2026, 9, 1, 8, 0, 0)),
        };

        var result = LoaderEndpoints.BuildLoaderShiftQueueData(cycles, [], LoaderNames);

        var l1 = result.Loaders.Single(l => l.LoaderName == "L1");
        Assert.Equal("T01", l1.Arrivals[0].TruckName);
        Assert.Equal("T02", l1.Arrivals[1].TruckName);
    }
}
