using BoxBox.Core.Analytics;
using static BoxBox.Tests.TestData;

namespace BoxBox.Tests;

public class AnomalyDetectorTests
{
    private static List<ConsumptionSegment> SegmentsWithLast(decimal lastLitres, int priorSegments = 7)
    {
        var fills = Steady(priorSegments + 1);
        var last = fills[^1];
        fills.Add(Fill(last.Date.AddDays(8).ToString("yyyy-MM-dd"), last.OdometerKm + 400, lastLitres));
        return ConsumptionCalculator.Segments(fills).ToList();
    }

    [Fact]
    public void Not_enough_history_means_no_flag()
    {
        var result = AnomalyDetector.Assess(SegmentsWithLast(60, priorSegments: 3));
        Assert.All(result, r => { Assert.Equal(AnomalyKind.None, r.Kind); Assert.False(r.HadEnoughHistory); });
    }

    [Fact]
    public void Steady_history_with_tiny_wobble_is_not_flagged()
    {
        var result = AnomalyDetector.Assess(SegmentsWithLast(28.4m));
        Assert.Equal(AnomalyKind.None, result[^1].Kind);
        Assert.True(result[^1].HadEnoughHistory);
    }

    [Fact]
    public void Big_jump_is_flagged_high()
    {
        var result = AnomalyDetector.Assess(SegmentsWithLast(36m)); // 9.0 vs 7.0 L/100km
        Assert.Equal(AnomalyKind.HighConsumption, result[^1].Kind);
        Assert.True(result[^1].RobustZ > 3.5);
    }

    [Fact]
    public void Big_drop_is_flagged_low()
    {
        var result = AnomalyDetector.Assess(SegmentsWithLast(20m)); // 5.0 L/100km
        Assert.Equal(AnomalyKind.LowConsumption, result[^1].Kind);
    }

    [Fact]
    public void Segment_larger_than_tank_is_a_possible_missed_fill_up_and_not_in_baseline()
    {
        var segs = SegmentsWithLast(60m);
        var result = AnomalyDetector.Assess(segs, new AnomalyOptions { TankCapacityLitres = 36m });
        Assert.Equal(AnomalyKind.PossibleMissedFillUp, result[^1].Kind);
    }
}
