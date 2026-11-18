using BoxBox.Core.Analytics;
using static BoxBox.Tests.TestData;

namespace BoxBox.Tests;

public class FillUpForecasterTests
{
    [Fact]
    public void Average_daily_km_is_total_distance_over_total_days()
    {
        var fills = Steady(6); // 5 gaps of 400 km / 8 days
        Assert.Equal(50.0, FillUpForecaster.AverageDailyKm(fills)!.Value, 6);
    }

    [Fact]
    public void Average_daily_km_needs_two_records()
    {
        Assert.Null(FillUpForecaster.AverageDailyKm([Fill("2026-01-01", 10_000, 30)]));
    }

    [Fact]
    public void Too_few_segments_gives_no_forecast()
    {
        Assert.Null(FillUpForecaster.Forecast(Steady(3)));
    }

    [Fact]
    public void Steady_driver_is_forecast_exactly_and_range_collapses()
    {
        var fills = Steady(6);
        var f = FillUpForecaster.Forecast(fills)!;
        var expected = fills[^1].Date.AddDays(8);
        Assert.Equal(expected, f.Expected);
        Assert.Equal(expected, f.Earliest);
        Assert.Equal(expected, f.Latest);
    }

    [Fact]
    public void Range_widens_with_variable_tanks()
    {
        var fills = new List<BoxBox.Core.Models.FillUp>
        {
            Fill("2026-01-01", 10_000, 30), Fill("2026-01-09", 10_300, 21),
            Fill("2026-01-17", 10_700, 28), Fill("2026-01-25", 11_200, 35),
            Fill("2026-02-02", 11_500, 21),
        };
        var f = FillUpForecaster.Forecast(fills)!;
        Assert.True(f.Earliest < f.Expected || f.Expected < f.Latest);
        Assert.True(f.Earliest <= f.Expected && f.Expected <= f.Latest);
    }
}
