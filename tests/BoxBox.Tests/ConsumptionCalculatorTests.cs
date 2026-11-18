using BoxBox.Core.Analytics;
using static BoxBox.Tests.TestData;

namespace BoxBox.Tests;

public class ConsumptionCalculatorTests
{
    [Fact]
    public void First_record_is_baseline_only()
    {
        Assert.Empty(ConsumptionCalculator.Segments([Fill("2026-01-01", 10_000, 30)]));
    }

    [Fact]
    public void Two_full_fill_ups_make_one_segment_using_the_second_fills_litres()
    {
        var s = Assert.Single(ConsumptionCalculator.Segments([
            Fill("2026-01-01", 10_000, 30), Fill("2026-01-09", 10_400, 28)]));
        Assert.Equal(400, s.DistanceKm);
        Assert.Equal(28m, s.Litres);
        Assert.Equal(7.0, s.LitresPer100Km, 3);
        Assert.Equal(8, s.Days);
    }

    [Fact]
    public void Partial_fills_are_summed_into_the_next_full_fill()
    {
        var s = Assert.Single(ConsumptionCalculator.Segments([
            Fill("2026-01-01", 10_000, 30),
            Fill("2026-01-04", 10_150, 10, full: false),
            Fill("2026-01-09", 10_400, 18)]));
        Assert.Equal(28m, s.Litres);
        Assert.Equal(2, s.FillUpCount);
        Assert.Equal(400, s.DistanceKm);
    }

    [Fact]
    public void Partial_fills_before_first_full_are_ignored()
    {
        var segs = ConsumptionCalculator.Segments([
            Fill("2025-12-20", 9_700, 10, full: false),
            Fill("2026-01-01", 10_000, 30),
            Fill("2026-01-09", 10_400, 28)]);
        Assert.Equal(28m, Assert.Single(segs).Litres);
    }

    [Fact]
    public void Trailing_partial_fill_is_not_yet_a_segment()
    {
        var segs = ConsumptionCalculator.Segments([
            Fill("2026-01-01", 10_000, 30),
            Fill("2026-01-09", 10_400, 28),
            Fill("2026-01-12", 10_500, 8, full: false)]);
        Assert.Single(segs);
    }

    [Fact]
    public void Input_order_does_not_matter()
    {
        var a = Steady(6);
        var b = Enumerable.Reverse(a).ToList();
        Assert.Equal(
            ConsumptionCalculator.Segments(a).Select(s => s.LitresPer100Km),
            ConsumptionCalculator.Segments(b).Select(s => s.LitresPer100Km));
    }

    [Fact]
    public void Cost_per_km_uses_litres_and_price()
    {
        var s = Assert.Single(ConsumptionCalculator.Segments([
            Fill("2026-01-01", 10_000, 30, 2.00m), Fill("2026-01-09", 10_400, 20, 2.00m)]));
        Assert.Equal(40.00m, s.Cost);
        Assert.Equal(0.10m, s.CostPerKm);
        Assert.Equal(20.0, s.KmPerLitre, 3);
    }
}
