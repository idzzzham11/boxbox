using BoxBox.Core.Analytics;
using BoxBox.Core.Models;
using BoxBox.Core.Validation;

namespace BoxBox.Tests;

public class SpendSummaryTests
{
    private static FuelEntry E(string date, decimal amount, decimal? litres = null) =>
        new(Guid.NewGuid(), TestData.Car, DateOnly.Parse(date), amount, litres);

    private static readonly DateOnly Today = new(2026, 11, 18);

    [Fact]
    public void Empty_gives_zeroes_and_nulls()
    {
        var r = SpendSummary.Compute([], Today);
        Assert.Equal(0m, r.TotalSpent);
        Assert.Equal(0m, r.ThisMonthSpent);
        Assert.Null(r.AveragePricePerLitre);
        Assert.Null(r.AverageMonthlySpent);
        Assert.Empty(r.Months);
    }

    [Fact]
    public void Groups_by_month_and_computes_this_and_last_month()
    {
        var r = SpendSummary.Compute([
            E("2026-09-05", 100), E("2026-10-02", 80), E("2026-10-20", 70), E("2026-11-03", 60)], Today);
        Assert.Equal(310m, r.TotalSpent);
        Assert.Equal(60m, r.ThisMonthSpent);
        Assert.Equal(150m, r.LastMonthSpent);
        Assert.Equal(3, r.Months.Count);
        Assert.Equal(2, r.Months[1].Count);
    }

    [Fact]
    public void Average_monthly_excludes_current_month()
    {
        var r = SpendSummary.Compute([E("2026-09-05", 100), E("2026-10-02", 200), E("2026-11-03", 999)], Today);
        Assert.Equal(150m, r.AverageMonthlySpent);
    }

    [Fact]
    public void Last_month_is_null_when_no_entries_then()
    {
        var r = SpendSummary.Compute([E("2026-08-05", 100)], Today);
        Assert.Null(r.LastMonthSpent);
    }

    [Fact]
    public void Price_per_litre_uses_only_entries_with_litres()
    {
        var r = SpendSummary.Compute([E("2026-10-01", 100), E("2026-10-10", 50, 25m), E("2026-10-20", 75, 25m)], Today);
        Assert.Equal(2.5m, r.AveragePricePerLitre); // (50+75)/(25+25)
        Assert.Equal(50m, r.TotalLitres);
        Assert.Equal(225m, r.TotalSpent);
    }

    [Fact]
    public void Entry_price_per_litre_derived()
    {
        Assert.Equal(2.05m, E("2026-10-01", 41, 20m).PricePerLitre);
        Assert.Null(E("2026-10-01", 41).PricePerLitre);
    }

    [Fact]
    public void Validator_rejects_bad_values()
    {
        Assert.Empty(FuelEntryValidator.Validate(E("2026-11-18", 50, 24m), Today));
        Assert.Empty(FuelEntryValidator.Validate(E("2026-11-18", 50), Today));
        Assert.NotEmpty(FuelEntryValidator.Validate(E("2026-11-18", 0), Today));
        Assert.NotEmpty(FuelEntryValidator.Validate(E("2026-11-18", 50, 0m), Today));
        Assert.NotEmpty(FuelEntryValidator.Validate(E("2026-11-19", 50), Today));
    }
}
