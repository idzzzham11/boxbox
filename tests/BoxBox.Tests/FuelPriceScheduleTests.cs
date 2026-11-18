using BoxBox.Core.Analytics;
using BoxBox.Core.Models;

namespace BoxBox.Tests;

public class FuelPriceScheduleTests
{
    private static FuelEntry E(string date, decimal amount, decimal? litres = null, int? trip = null) =>
        new(Guid.NewGuid(), TestData.Car, DateOnly.Parse(date), amount, litres, trip);

    private static readonly FuelPriceSchedule Budi = new([new PricePeriod(new DateOnly(2025, 9, 30), 1.99m)]);

    [Fact]
    public void Estimates_litres_from_amount_and_price()
    {
        var r = Budi.Resolve(E("2026-11-06", 60.10m));
        Assert.True(r.Estimated);
        Assert.Equal(30.20m, r.Litres);        // 60.10 / 1.99 = 30.2010 -> 30.20 (2 d.p.)
        Assert.Equal(1.99m, r.PricePerLitre);
    }

    [Fact]
    public void Recorded_litres_always_win_over_the_estimate()
    {
        var r = Budi.Resolve(E("2026-11-06", 60m, 29.4m));
        Assert.False(r.Estimated);
        Assert.Equal(29.4m, r.Litres);
    }

    [Fact]
    public void No_estimate_before_the_first_period_or_with_an_empty_schedule()
    {
        Assert.Null(Budi.Resolve(E("2025-09-29", 50m)).Litres);
        Assert.Null(FuelPriceSchedule.Empty.Resolve(E("2026-11-06", 50m)).Litres);
    }

    [Fact]
    public void Uses_the_price_in_force_on_each_date()
    {
        var s = new FuelPriceSchedule([
            new PricePeriod(new DateOnly(2026, 6, 1), 2.50m),   // deliberately unsorted input
            new PricePeriod(new DateOnly(2025, 9, 30), 1.99m)]);
        Assert.Equal(1.99m, s.PriceOn(new DateOnly(2026, 5, 31)));
        Assert.Equal(2.50m, s.PriceOn(new DateOnly(2026, 6, 1)));
    }

    [Fact]
    public void Summary_total_litres_include_estimates_but_average_price_does_not()
    {
        var s = SpendSummary.Compute([
            E("2026-11-01", 50m),                // est. 25.13 L
            E("2026-11-10", 60m, 28m)],          // recorded
            new DateOnly(2026, 11, 18), Budi);
        Assert.Equal(25.13m + 28m, s.TotalLitres);
        Assert.Equal(1, s.EstimatedLitresEntries);
        Assert.Equal(2.143m, s.AveragePricePerLitre); // 60 / 28 only
    }

    [Fact]
    public void Economy_works_from_estimated_litres_and_trip_km()
    {
        var r = FuelEconomy.Compute([
            E("2026-11-01", 59.7m, null, 400),   // starting point
            E("2026-11-10", 59.7m, null, 400)],  // 30 L over 400 km
            Budi);
        Assert.Equal(1, r.SegmentCount);
        Assert.Equal(7.50m, r.AverageLitresPer100Km);
    }
}
