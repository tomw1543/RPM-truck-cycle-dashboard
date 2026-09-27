using HaulCycle.Api.Kpi;

namespace HaulCycle.Api.Tests;

public class MoveReasonCalculatorTests
{
    private static readonly RouteReferenceRow[] Routes =
    {
        TestData.RouteReference("L1 to ROM pad", "L1", "ROM pad", 3.2m),
        TestData.RouteReference("L1 to Waste dump", "L1", "Waste dump", 6.0m),
        TestData.RouteReference("L1 to Crusher", "L1", "Crusher", 5.0m),
        TestData.RouteReference("L2 to ROM pad", "L2", "ROM pad", 5.2m),
        TestData.RouteReference("L2 to Waste dump", "L2", "Waste dump", 4.5m),
        TestData.RouteReference("L2 to Crusher", "L2", "Crusher", 6.2m),
        TestData.RouteReference("L3 to ROM pad", "L3", "ROM pad", 4.5m),
        TestData.RouteReference("L3 to Crusher", "L3", "Crusher", 6.0m),
    };

    private static MoveReasonCalculator.Result Run(
        OptimisedAssignmentRow original, OptimisedAssignmentRow candidate,
        OptimisedLoaderStatRow[] originalStats, OptimisedLoaderStatRow[] candidateStats,
        OptimisedPlanRow? originalPlan = null, OptimisedPlanRow? candidatePlan = null) =>
        MoveReasonCalculator.Calculate(
            new[] { original }, new[] { candidate }, originalStats, candidateStats,
            originalPlan ?? TestData.OptimisedPlan("Original"), candidatePlan ?? TestData.OptimisedPlan("MoreOutput"), Routes);

    [Fact]
    public void StoodDownTruck_StatesDestinationsCoveredAndQueueDrop()
    {
        var original = TestData.OptimisedAssignment("Original", "T01", "L1 to ROM pad", "L1", "ROM pad");
        var candidate = TestData.OptimisedAssignment("MoreOutput", "T01", null, null, null, isStoodDown: true);

        var originalStats = new[] { TestData.OptimisedLoaderStat("Original", "L1", 5, avgQueueMin: 5.0m, utilisation: 0.5m) };
        var candidateStats = new[] { TestData.OptimisedLoaderStat("MoreOutput", "L1", 4, avgQueueMin: 2.0m, utilisation: 0.6m) };

        var originalPlan = TestData.OptimisedPlan("Original", crusherTonnesMean: 100m, romTonnesMean: 100m, wasteTonnesMean: 100m);
        var candidatePlan = TestData.OptimisedPlan("MoreOutput", crusherTonnesMean: 100m, romTonnesMean: 100m, wasteTonnesMean: 100m);

        var result = Run(original, candidate, originalStats, candidateStats, originalPlan, candidatePlan);

        Assert.Equal(
            "Stood down: the other trucks covered its work, with every destination at or above its original tonnes. " +
            "L1's average queue fell from 5.0 to 2.0 min.",
            result.ReasonsByTruck["T01"]);
    }

    [Fact]
    public void DifferentLoader_GenuineQueueRelief_NoSpareCapacityClaim()
    {
        // Same destination (Crusher) on both sides, so no destination-binding clause can fire,
        // and the new route (L3 to Crusher, 6.0 km) is longer than the old one (L1 to Crusher,
        // 5.0 km), so no route-length clause either - isolates queue relief.
        var original = TestData.OptimisedAssignment("Original", "T02", "L1 to Crusher", "L1", "Crusher");
        var candidate = TestData.OptimisedAssignment("MoreOutput", "T02", "L3 to Crusher", "L3", "Crusher");

        var originalStats = new[]
        {
            TestData.OptimisedLoaderStat("Original", "L1", 7, avgQueueMin: 6.1m, utilisation: 0.30m),
            TestData.OptimisedLoaderStat("Original", "L3", 3, avgQueueMin: 1.7m, utilisation: 0.66m), // higher util than L1 - no spare capacity
        };
        var candidateStats = originalStats; // unused for this reason beyond lookup by name

        var result = Run(original, candidate, originalStats, candidateStats);

        Assert.Equal("Leaves L1 (6.1 min average queue) for L3 (1.7 min).", result.ReasonsByTruck["T02"]);
    }

    [Fact]
    public void ReportedBadCase_UnderUsedToBusierLoader_ClaimsNeitherReliefNorCapacity()
    {
        // T03's real MoreOutput move: L1 (20% utilised, 1.7 min queue) to L2 (51% utilised).
        // Same destination and a longer route, so no other clause can rescue a false claim -
        // this must fall back rather than invent a reason.
        var original = TestData.OptimisedAssignment("Original", "T03", "L1 to ROM pad", "L1", "ROM pad");
        var candidate = TestData.OptimisedAssignment("MoreOutput", "T03", "L2 to ROM pad", "L2", "ROM pad");

        var originalStats = new[]
        {
            TestData.OptimisedLoaderStat("Original", "L1", 2, avgQueueMin: 1.7m, utilisation: 0.20m),
            TestData.OptimisedLoaderStat("Original", "L2", 6, avgQueueMin: 4.0m, utilisation: 0.51m),
        };

        var result = Run(original, candidate, originalStats, originalStats);

        var reason = result.ReasonsByTruck["T03"];
        Assert.DoesNotContain("queue", reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("less busy", reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("spare", reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Part of a wider rebalance; see the loader table.", reason);
    }

    [Fact]
    public void DifferentLoader_ShorterRoute()
    {
        var original = TestData.OptimisedAssignment("Original", "T04", "L3 to Crusher", "L3", "Crusher");
        var candidate = TestData.OptimisedAssignment("MoreOutput", "T04", "L1 to Crusher", "L1", "Crusher");

        var originalStats = new[]
        {
            TestData.OptimisedLoaderStat("Original", "L3", 5, avgQueueMin: 2.0m, utilisation: 0.3m),
            TestData.OptimisedLoaderStat("Original", "L1", 4, avgQueueMin: 1.8m, utilisation: 0.5m), // no relief, no spare capacity
        };

        var result = Run(original, candidate, originalStats, originalStats);

        Assert.Equal("L1 to Crusher is 1.0 km shorter than L3 to Crusher.", result.ReasonsByTruck["T04"]);
    }

    [Fact]
    public void DifferentLoader_BindingDestination()
    {
        var original = TestData.OptimisedAssignment("Original", "T05", "L1 to ROM pad", "L1", "ROM pad");
        var candidate = TestData.OptimisedAssignment("MoreOutput", "T05", "L2 to Crusher", "L2", "Crusher");

        var originalStats = new[]
        {
            TestData.OptimisedLoaderStat("Original", "L1", 3, avgQueueMin: 2.0m, utilisation: 0.4m),
            TestData.OptimisedLoaderStat("Original", "L2", 4, avgQueueMin: 3.0m, utilisation: 0.5m), // no relief, no spare capacity
        };

        var originalPlan = TestData.OptimisedPlan("Original", crusherTonnesMean: 100m);
        var candidatePlan = TestData.OptimisedPlan("MoreOutput", crusherTonnesMean: 103m); // 3% above - within the 5% binding band

        var result = Run(original, candidate, originalStats, originalStats, originalPlan, candidatePlan);

        Assert.Equal("Moves to Crusher to keep it at its original tonnes.", result.ReasonsByTruck["T05"]);
    }

    [Fact]
    public void SameLoader_DifferentDestination_WithShorterRoute()
    {
        var original = TestData.OptimisedAssignment("Original", "T06", "L2 to ROM pad", "L2", "ROM pad");
        var candidate = TestData.OptimisedAssignment("MoreOutput", "T06", "L2 to Waste dump", "L2", "Waste dump");

        var stats = new[] { TestData.OptimisedLoaderStat("Original", "L2", 4, avgQueueMin: 2.0m, utilisation: 0.4m) };

        var result = Run(original, candidate, stats, stats);

        Assert.Equal(
            "Stays on L2, switches to Waste dump. L2 to Waste dump is 0.7 km shorter than L2 to ROM pad.",
            result.ReasonsByTruck["T06"]);
    }

    [Fact]
    public void NoRuleApplies_Fallback()
    {
        var original = TestData.OptimisedAssignment("Original", "T07", "L1 to Crusher", "L1", "Crusher");
        var candidate = TestData.OptimisedAssignment("MoreOutput", "T07", "L2 to Crusher", "L2", "Crusher");

        // Same destination, longer route, equal queue and utilisation - nothing to say.
        var originalStats = new[]
        {
            TestData.OptimisedLoaderStat("Original", "L1", 3, avgQueueMin: 3.0m, utilisation: 0.4m),
            TestData.OptimisedLoaderStat("Original", "L2", 3, avgQueueMin: 3.0m, utilisation: 0.4m),
        };

        var result = Run(original, candidate, originalStats, originalStats);

        Assert.Equal("Part of a wider rebalance; see the loader table.", result.ReasonsByTruck["T07"]);
    }

    [Fact]
    public void UnchangedTruck_GetsNoReason()
    {
        var original = TestData.OptimisedAssignment("Original", "T08", "L1 to ROM pad", "L1", "ROM pad");
        var candidate = TestData.OptimisedAssignment("MoreOutput", "T08", "L1 to ROM pad", "L1", "ROM pad");

        var stats = new[] { TestData.OptimisedLoaderStat("Original", "L1", 3, avgQueueMin: 2.0m, utilisation: 0.4m) };

        var result = Run(original, candidate, stats, stats);

        Assert.False(result.ReasonsByTruck.ContainsKey("T08"));
    }

    [Fact]
    public void Summary_WithStandDowns()
    {
        var originalStats = new[]
        {
            TestData.OptimisedLoaderStat("Original", "L1", 3, avgQueueMin: 1.7m, utilisation: 0.20m),
            TestData.OptimisedLoaderStat("Original", "L2", 5, avgQueueMin: 4.3m, utilisation: 0.51m),
            TestData.OptimisedLoaderStat("Original", "L3", 5, avgQueueMin: 6.1m, utilisation: 0.66m),
        };
        var candidateStats = new[]
        {
            TestData.OptimisedLoaderStat("Leaner", "L1", 5, avgQueueMin: 2.5m, utilisation: 0.65m),
            TestData.OptimisedLoaderStat("Leaner", "L2", 4, avgQueueMin: 2.7m, utilisation: 0.41m),
            TestData.OptimisedLoaderStat("Leaner", "L3", 2, avgQueueMin: 2.2m, utilisation: 0.30m),
        };

        var candidatePlan = TestData.OptimisedPlan("Leaner", trucksStoodDownMean: 3m, trucksStoodDownMin: 3m, trucksStoodDownMax: 3m);

        var result = MoveReasonCalculator.Calculate(
            Array.Empty<OptimisedAssignmentRow>(), Array.Empty<OptimisedAssignmentRow>(),
            originalStats, candidateStats, TestData.OptimisedPlan("Original"), candidatePlan, Routes);

        Assert.Equal(
            "L2 lost 1 truck (average queue 4.3 to 2.7 min) and L3 lost 3 (average queue 6.1 to 2.2 min); L1 gained 2 trucks (utilisation 20% to 65%). Stood down 3 trucks.",
            result.Summary);
    }

    [Fact]
    public void Summary_WithoutStandDowns()
    {
        var originalStats = new[]
        {
            TestData.OptimisedLoaderStat("Original", "L1", 3, avgQueueMin: 1.7m, utilisation: 0.20m),
            TestData.OptimisedLoaderStat("Original", "L3", 5, avgQueueMin: 6.1m, utilisation: 0.66m),
        };
        var candidateStats = new[]
        {
            TestData.OptimisedLoaderStat("MoreOutput", "L1", 5, avgQueueMin: 2.5m, utilisation: 0.65m),
            TestData.OptimisedLoaderStat("MoreOutput", "L3", 3, avgQueueMin: 2.2m, utilisation: 0.40m),
        };

        var candidatePlan = TestData.OptimisedPlan("MoreOutput", trucksStoodDownMean: 0m, trucksStoodDownMin: 0m, trucksStoodDownMax: 0m);

        var result = MoveReasonCalculator.Calculate(
            Array.Empty<OptimisedAssignmentRow>(), Array.Empty<OptimisedAssignmentRow>(),
            originalStats, candidateStats, TestData.OptimisedPlan("Original"), candidatePlan, Routes);

        Assert.Equal(
            "L3 lost 2 trucks (average queue 6.1 to 2.2 min); L1 gained 2 trucks (utilisation 20% to 65%).",
            result.Summary);
    }

    [Fact]
    public void Summary_NoChanges_SameAsOriginal()
    {
        var stats = new[] { TestData.OptimisedLoaderStat("Original", "L1", 3, avgQueueMin: 1.7m, utilisation: 0.20m) };
        var candidatePlan = TestData.OptimisedPlan("MoreOutput", trucksStoodDownMean: 0m, trucksStoodDownMin: 0m, trucksStoodDownMax: 0m);

        var result = MoveReasonCalculator.Calculate(
            Array.Empty<OptimisedAssignmentRow>(), Array.Empty<OptimisedAssignmentRow>(),
            stats, stats, TestData.OptimisedPlan("Original"), candidatePlan, Routes);

        Assert.Equal("Same as the original schedule.", result.Summary);
    }
}
