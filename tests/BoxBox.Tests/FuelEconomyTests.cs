using BoxBox.Core.Analytics;
using BoxBox.Core.Models;
using BoxBox.Core.Validation;

namespace BoxBox.Tests;

public class FuelEconomyTests
{
    private static FuelEntry E(string date, decimal amount, decimal? litres, int? trip) =>
        new(Guid.NewGuid(), TestData.Car, DateOnly.Parse(date), amount, litres, trip);

    [Fact]
    public void No_data_gives_empty_result()
    {
        var r = FuelEconomy.Compute([]);
        Assert.Equal(0, r.SegmentCount);
        Assert.Null(r.AverageLitresPer100Km);
        Assert.Null(r.Last);
    }

    [Fact]
    public void First_complete_fill_is_only_a_starting_point()
    {
        var r = FuelEconomy.Compute([E("2026-11-01", 60, 29m, 400)]);
        Assert.Equal(0, r.SegmentCount);
    }

    [Fact]
    public void Second_complete_fill_gives_consumption_and_cost_per_km()
    {
        var r = FuelEconomy.Compute([
            E("2026-11-01", 60, 29m, 410),   // starting point
            E("2026-11-10", 56, 28m, 400)]); // 28 L over 400 km
        Assert.Equal(1, r.SegmentCount);
        Assert.Equal(7.00m, r.AverageLitresPer100Km);
        Assert.Equal(14.29m, r.AverageKmPerLitre);
        Assert.Equal(0.140m, r.AverageCostPerKm); // RM56 / 400 km
        Assert.Equal(400, r.TotalKm);
    }

    [Fact]
    public void Every_following_fill_is_its_own_measurement()
    {
        var r = FuelEconomy.Compute([
            E("2026-11-01", 60, 29m, 410),
            E("2026-11-09", 56, 28m, 400),
            E("2026-11-17", 62, 30m, 420)]);
        Assert.Equal(2, r.SegmentCount);
        Assert.Equal(7.00m, r.Segments[0].LitresPer100Km);
        Assert.Equal(7.14m, r.Segments[1].LitresPer100Km);
        Assert.Equal(r.Segments[1], r.Last);
    }

    [Fact]
    public void Amount_only_history_never_acts_as_a_starting_point()
    {
        var r = FuelEconomy.Compute([
            E("2026-11-14", 30, null, null),   // old record, tank level unknown
            E("2026-11-15", 60, 29m, 410)]);   // first complete: starting point only
        Assert.Equal(0, r.SegmentCount);
    }

    [Fact]
    public void Missing_data_breaks_the_chain_then_it_restarts()
    {
        var r = FuelEconomy.Compute([
            E("2026-09-01", 50, 25m, 350),
            E("2026-09-10", 50, 25m, 360),     // measured: 25 L / 360 km
            E("2026-09-20", 20, 10m, null),    // forgot trip km -> unknown
            E("2026-10-01", 50, 25m, 350),     // starting point again
            E("2026-10-10", 52, 26m, 370)]);   // measured: 26 L / 370 km
        Assert.Equal(2, r.SegmentCount);
        Assert.Equal(25m, r.Segments[0].Litres);
        Assert.Equal(26m, r.Segments[1].Litres);
    }

    [Fact]
    public void Averages_are_distance_weighted_not_mean_of_ratios()
    {
        var r = FuelEconomy.Compute([
            E("2026-11-01", 60, 29m, 400),
            E("2026-11-05", 10, 5m, 100),    // 5.0 L/100km over 100 km
            E("2026-11-12", 80, 40m, 500)]); // 8.0 L/100km over 500 km
        Assert.Equal(45m / 600m * 100m, r.AverageLitresPer100Km!.Value, 2); // 7.50, not 6.50
    }

    [Fact]
    public void Validator_checks_trip_km()
    {
        var today = new DateOnly(2026, 11, 18);
        Assert.Empty(FuelEntryValidator.Validate(E("2026-11-18", 50, 24m, 400), today));
        Assert.NotEmpty(FuelEntryValidator.Validate(E("2026-11-18", 50, 24m, 0), today));
        Assert.NotEmpty(FuelEntryValidator.Validate(E("2026-11-18", 50, 24m, 5000), today));
    }

    [Fact]
    public void Summary_includes_economy()
    {
        var s = SpendSummary.Compute([
            E("2026-11-01", 60, 29m, 400), E("2026-11-10", 56, 28m, 400)], new DateOnly(2026, 11, 18));
        Assert.Equal(7.00m, s.Economy.AverageLitresPer100Km);
    }
}
