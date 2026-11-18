using BoxBox.Core.Analytics;
using BoxBox.Core.Models;

namespace BoxBox.Tests;

public class SpendForecasterTests
{
    private static FuelEntry E(string date, decimal amount, decimal? litres = null) =>
        new(Guid.NewGuid(), TestData.Car, DateOnly.Parse(date), amount, litres);

    private static readonly DateOnly Today = new(2026, 11, 18);
    private static readonly FuelPriceSchedule Budi = new([new PricePeriod(new DateOnly(2025, 9, 30), 1.99m)]);

    private static List<FuelEntry> Every(int days, decimal amount, int n, string start = "2026-09-01") =>
        Enumerable.Range(0, n).Select(i => E(DateOnly.Parse(start).AddDays(i * days).ToString("yyyy-MM-dd"), amount)).ToList();

    // Invented but realistic data: irregular gaps and amounts that swing between small and large fill-ups.
    private static List<FuelEntry> Sample() => [
        E("2026-09-03", 25), E("2026-09-08", 48.5m), E("2026-09-13", 52), E("2026-09-14", 18),
        E("2026-09-20", 55.2m), E("2026-09-28", 40), E("2026-10-07", 61.3m), E("2026-10-15", 35),
        E("2026-10-27", 50.75m), E("2026-11-02", 12), E("2026-11-08", 58), E("2026-11-15", 27.4m)];

    [Fact]
    public void Too_few_fill_ups_gives_no_forecast_and_says_why()
    {
        var r = SpendForecaster.Compute(Every(7, 50, 5), Today);
        Assert.False(r.Available);
        Assert.Contains("at least 6", r.Reason);
        Assert.Null(r.NextDate);
    }

    [Fact]
    public void Perfectly_regular_history_is_predicted_exactly()
    {
        // Every 7 days from 1 Sep: ..., 3 Nov (n=10 -> last = 3 Nov). Next = 10 Nov, clamped to today.
        var r = SpendForecaster.Compute(Every(7, 50, 10, "2026-09-01"), new DateOnly(2026, 11, 4));
        Assert.True(r.Available);
        Assert.Equal(7, r.IntervalDays!.Predicted);
        Assert.Equal(50, r.Amount!.Predicted);
        Assert.Equal(0, r.Amount.ModelMae, 6);
        Assert.Equal(new DateOnly(2026, 11, 3).AddDays(7), r.NextDate);
        Assert.False(r.Amount.BeatsNaive); // a tie with the naive guess is not "beating" it
    }

    [Fact]
    public void Chosen_model_is_never_worse_than_naive_in_the_backtest()
    {
        var r = SpendForecaster.Compute(Sample(), Today);
        Assert.True(r.IntervalDays!.ModelMae <= r.IntervalDays.NaiveMae + 1e-9);
        Assert.True(r.Amount!.ModelMae <= r.Amount.NaiveMae + 1e-9);
    }

    [Fact]
    public void Backtest_scores_one_prediction_per_value_after_the_warm_up()
    {
        var r = SpendForecaster.Compute(Sample(), Today); // 12 fills -> 11 intervals, warm-up 3 -> 8 scored
        Assert.Equal(8, r.IntervalDays!.Evaluations);
        Assert.Equal(9, r.Amount!.Evaluations);          // 12 amounts, warm-up 3
    }

    [Fact]
    public void Range_brackets_the_forecast_and_is_never_negative()
    {
        var r = SpendForecaster.Compute(Sample(), Today);
        foreach (var s in new[] { r.IntervalDays!, r.Amount! })
        {
            Assert.True(s.Low >= 0);
            Assert.True(s.Low <= s.Predicted && s.Predicted <= s.High);
        }
    }

    [Fact]
    public void Next_date_is_never_in_the_past()
    {
        var r = SpendForecaster.Compute(Sample(), new DateOnly(2026, 12, 30)); // long overdue
        Assert.Equal(new DateOnly(2026, 12, 30), r.NextDate);
    }

    [Fact]
    public void Projected_month_spend_includes_what_is_already_spent()
    {
        var r = SpendForecaster.Compute(Sample(), Today);
        Assert.True(r.ProjectedMonthSpend >= 12m + 58m + 27.4m); // November so far = RM 97.40
    }

    [Fact]
    public void Litres_forecast_is_flagged_as_derived_when_all_litres_are_estimates()
    {
        var r = SpendForecaster.Compute(Sample(), Today, Budi);
        Assert.NotNull(r.Litres);
        Assert.True(r.LitresDerivedFromPrice);
        Assert.Equal(r.Amount!.Predicted / 1.99, r.Litres!.Predicted, 0);
    }

    [Fact]
    public void No_litres_forecast_without_litres_or_a_price()
    {
        var r = SpendForecaster.Compute(Sample(), Today);
        Assert.Null(r.Litres);
        Assert.False(r.LitresDerivedFromPrice);
    }

    [Fact]
    public void Recorded_litres_are_not_flagged_as_derived()
    {
        var entries = Every(7, 50, 8).Select(e => e with { Litres = 25m }).ToList();
        var r = SpendForecaster.Compute(entries, Today, Budi);
        Assert.False(r.LitresDerivedFromPrice);
        Assert.Equal(25, r.Litres!.Predicted, 6);
    }

    [Theory]
    [InlineData(6, "Very low")]
    [InlineData(12, "Low")]
    [InlineData(25, "Moderate")]
    [InlineData(60, "Good")]
    public void Confidence_grows_with_history(int n, string expected) =>
        Assert.Equal(expected, SpendForecaster.Compute(Every(3, 40, n), new DateOnly(2027, 6, 1)).Confidence);
}
