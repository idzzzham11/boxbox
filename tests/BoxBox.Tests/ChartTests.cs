using System.Globalization;
using BoxBox.Core.Analytics;
using BoxBox.Core.Contracts;
using BoxBox.Core.Models;
using BoxBox.Web.Components;
using Bunit;

namespace BoxBox.Tests;

public class ChartScaleTests
{
    [Fact]
    public void Money_scale_starts_at_zero_and_uses_round_steps()
    {
        var s = NiceScale.For(10, 60, zeroBased: true);
        Assert.Equal(0, s.Min);
        Assert.Equal(60, s.Max);
        Assert.Equal(20, s.Step);
        Assert.Equal([0d, 20d, 40d, 60d], s.Ticks().ToArray());
    }

    [Fact]
    public void Zero_based_axis_starts_at_zero_even_when_all_values_are_large()
    {
        Assert.Equal(0, NiceScale.For(25, 55, zeroBased: true).Min);
    }

    [Fact]
    public void Economy_scale_hugs_the_data_instead_of_starting_at_zero()
    {
        var s = NiceScale.For(7.07, 8.57, zeroBased: false);
        Assert.True(s.Min <= 7.07 && s.Max >= 8.57);
        Assert.True(s.Min > 5, "a non-zero-based axis should not drag down to 0");
        Assert.InRange(s.Ticks().Count(), 2, 8);
    }

    [Fact]
    public void Flat_data_still_gets_a_usable_range()
    {
        var s = NiceScale.For(5, 5, zeroBased: false);
        Assert.True(s.Max > s.Min);
        Assert.True(s.Min <= 5 && s.Max >= 5);
        var y = s.Y(5, 10, 100);
        Assert.False(double.IsNaN(y));
        Assert.InRange(y, 10, 100);   // the single value lands inside the plot area
    }

    [Fact]
    public void Y_maps_max_to_the_top_and_min_to_the_bottom()
    {
        var s = NiceScale.For(0, 60, zeroBased: true);
        Assert.Equal(14, s.Y(60, 14, 128), 6);
        Assert.Equal(128, s.Y(0, 14, 128), 6);
        Assert.Equal(71, s.Y(30, 14, 128), 6);
    }

    [Fact]
    public void X_is_proportional_to_the_date_and_a_single_date_is_centred()
    {
        var a = new DateOnly(2026, 11, 1); var b = new DateOnly(2026, 11, 11);
        Assert.Equal(40, ChartMath.X(a, a, b, 40, 300), 6);
        Assert.Equal(300, ChartMath.X(b, a, b, 40, 300), 6);
        Assert.Equal(170, ChartMath.X(new DateOnly(2026, 11, 6), a, b, 40, 300), 6);
        Assert.Equal(170, ChartMath.X(a, a, a, 40, 300), 6);
    }
}

public class ChartComponentTests : BunitContext
{
    private static ChartPoint P(string date, double v) => new(DateOnly.Parse(date), v, $"{date} · {v}");

    private static double Attr(AngleSharp.Dom.IElement e, string name) =>
        double.Parse(e.GetAttribute(name)!, CultureInfo.InvariantCulture);

    [Fact]
    public void Empty_series_draws_nothing_at_all()
    {
        var cut = Render<LineChart>(p => p
            .Add(x => x.Points, [])
            .Add(x => x.AriaLabel, "test"));
        Assert.Equal("", cut.Markup.Trim());
    }

    [Fact]
    public void Draws_one_dot_per_point_and_a_line_through_them()
    {
        var cut = Render<LineChart>(p => p
            .Add(x => x.Points, [P("2026-11-01", 20), P("2026-11-05", 60), P("2026-11-09", 35)])
            .Add(x => x.AriaLabel, "spend"));
        Assert.Equal(3, cut.FindAll("circle").Count);
        var points = cut.Find("polyline").GetAttribute("points")!.Split(' ');
        Assert.Equal(3, points.Length);
        Assert.Equal("img", cut.Find("svg").GetAttribute("role"));
        Assert.Equal("spend", cut.Find("svg").GetAttribute("aria-label"));
    }

    [Fact]
    public void A_single_point_is_just_a_dot_with_no_line()
    {
        var cut = Render<LineChart>(p => p
            .Add(x => x.Points, [P("2026-11-15", 8.6)])
            .Add(x => x.AriaLabel, "economy"));
        Assert.Single(cut.FindAll("circle"));
        Assert.Empty(cut.FindAll("polyline"));
    }

    [Fact]
    public void Every_dot_stays_inside_the_plot_area()
    {
        var pts = Enumerable.Range(0, 30).Select(i => P(new DateOnly(2026, 9, 1).AddDays(i * 3).ToString("yyyy-MM-dd"), 10 + (i * 37) % 55)).ToList();
        var cut = Render<LineChart>(p => p.Add(x => x.Points, pts).Add(x => x.ZeroBased, true).Add(x => x.AriaLabel, "x"));
        foreach (var c in cut.FindAll("circle"))
        {
            Assert.InRange(Attr(c, "cx"), 40, 308);   // plot area: left margin 40, right edge 320 - 12
            Assert.InRange(Attr(c, "cy"), 14, 128);   // top margin 14, bottom 150 - 22
        }
    }

    [Fact]
    public void Unsorted_input_is_drawn_in_date_order()
    {
        var cut = Render<LineChart>(p => p
            .Add(x => x.Points, [P("2026-11-09", 35), P("2026-11-01", 20), P("2026-11-05", 60)])
            .Add(x => x.AriaLabel, "x"));
        var xs = cut.Find("polyline").GetAttribute("points")!.Split(' ')
            .Select(pair => double.Parse(pair.Split(',')[0], CultureInfo.InvariantCulture)).ToList();
        Assert.Equal(xs.OrderBy(v => v), xs);
    }

    [Fact]
    public void Average_line_and_tooltips_and_latest_value_are_drawn()
    {
        var cut = Render<LineChart>(p => p
            .Add(x => x.Points, [P("2026-11-01", 7.0), P("2026-11-09", 8.6)])
            .Add(x => x.AverageLine, 7.8)
            .Add(x => x.Format, v => v.ToString("0.0", CultureInfo.InvariantCulture))
            .Add(x => x.AriaLabel, "x"));
        Assert.Single(cut.FindAll("line.avg"));
        Assert.Contains("2026-11-09 · 8.6", cut.Markup);   // tooltip (<title>) of the latest point
        Assert.Contains("8.6", cut.Find("text.latest").TextContent);
    }

    [Fact]
    public void Decimal_separators_are_always_dots_so_svg_stays_valid()
    {
        var cut = Render<LineChart>(p => p
            .Add(x => x.Points, [P("2026-11-01", 7.05), P("2026-11-09", 8.65)])
            .Add(x => x.AriaLabel, "x"));
        Assert.DoesNotContain(",", cut.Find("circle").GetAttribute("cx")!);
        Assert.DoesNotContain(",", cut.Find("circle").GetAttribute("cy")!);
    }

    private static List<EntryDto> Entries(int n) =>
        Enumerable.Range(0, n).Select(i => new EntryDto(Guid.NewGuid(), new DateOnly(2026, 8, 1).AddDays(i * 2), 20 + i, null, null, null, false))
            .OrderByDescending(e => e.Date).ToList(); // the API returns newest first

    [Fact]
    public void Price_card_charts_the_latest_30_fill_ups()
    {
        var entries = Entries(40);
        // A summary built from the same fill-ups, so the month bars have data too.
        var summary = SpendSummary.Compute(
            entries.Select(e => new FuelEntry(e.Id, TestData.Car, e.Date, e.AmountPaid)), new DateOnly(2026, 11, 18));
        var cut = Render<PriceCard>(p => p.Add(x => x.Summary, summary).Add(x => x.Entries, entries));
        Assert.Equal(30, cut.FindAll("svg.chart circle").Count);
        Assert.Contains("Spend per fill-up", cut.Markup);
        Assert.Contains("Spending by month", cut.Markup);
    }

    [Fact]
    public void Price_card_with_no_fill_ups_shows_figures_but_no_graph_headings_or_messages()
    {
        var summary = SpendSummary.Compute([], new DateOnly(2026, 11, 18));
        var cut = Render<PriceCard>(p => p.Add(x => x.Summary, summary).Add(x => x.Entries, []));
        Assert.Empty(cut.FindAll("svg.chart"));
        Assert.DoesNotContain("Spend per fill-up", cut.Markup);
        Assert.DoesNotContain("Spending by month", cut.Markup);
        Assert.DoesNotContain("No ", cut.Markup);        // no "No data yet" style messages
        Assert.Equal(4, cut.FindAll(".stat").Count);
    }

    private static FuelEntry E(string date, decimal amount, int? trip) =>
        new(Guid.NewGuid(), TestData.Car, DateOnly.Parse(date), amount, 25m, trip);

    [Fact]
    public void Litres_card_graphs_fuel_economy_with_an_average_line_once_trip_km_exist()
    {
        var summary = SpendSummary.Compute([
            E("2026-11-01", 50, 400), E("2026-11-08", 52, 380), E("2026-11-15", 55, 360)], new DateOnly(2026, 11, 18));
        Assert.Equal(2, summary.Economy.SegmentCount);
        var cut = Render<LitresCard>(p => p.Add(x => x.Summary, summary));
        Assert.Equal(2, cut.FindAll("svg.chart circle").Count);
        Assert.Single(cut.FindAll("svg.chart line.avg"));
        Assert.Contains("Fuel economy per tank", cut.Markup);
    }

    [Fact]
    public void Litres_card_hides_the_economy_graph_and_its_heading_when_there_is_no_trip_km()
    {
        var summary = SpendSummary.Compute([E("2026-11-01", 50, null)], new DateOnly(2026, 11, 18));
        var cut = Render<LitresCard>(p => p.Add(x => x.Summary, summary));
        Assert.Empty(cut.FindAll("svg.chart"));
        Assert.DoesNotContain("Fuel economy per tank", cut.Markup);
        Assert.DoesNotContain("trip km", cut.Markup);     // no instructions either
    }

    // ---- Litres card figures (same four as the Price card) ----

    private static readonly FuelPriceSchedule Budi = new([new PricePeriod(new DateOnly(2025, 9, 30), 1.99m)]);
    private static readonly DateOnly Today = new(2026, 11, 18);

    private static List<string> StatValues(IRenderedComponent<LitresCard> cut) =>
        cut.FindAll(".stat").Select(s => s.TextContent.Trim()).ToList();

    [Fact]
    public void Litres_card_shows_this_month_last_month_average_and_total()
    {
        var summary = SpendSummary.Compute([
            new FuelEntry(Guid.NewGuid(), TestData.Car, new DateOnly(2026, 9, 10), 99.5m, 50m),
            new FuelEntry(Guid.NewGuid(), TestData.Car, new DateOnly(2026, 10, 10), 59.7m, 30m),
            new FuelEntry(Guid.NewGuid(), TestData.Car, new DateOnly(2026, 11, 10), 39.8m, 20m)], Today);
        var cut = Render<LitresCard>(p => p.Add(x => x.Summary, summary));
        var stats = StatValues(cut);

        Assert.Contains(stats, s => s.StartsWith("20.0 L") && s.Contains("This month"));
        Assert.Contains(stats, s => s.StartsWith("30.0 L") && s.Contains("Last month"));
        Assert.Contains(stats, s => s.StartsWith("40.0 L") && s.Contains("Avg / month"));   // (50 + 30) / 2
        Assert.Contains(stats, s => s.StartsWith("100.0 L") && s.Contains("Total litres"));
    }

    [Fact]
    public void Litres_card_shows_dashes_when_no_litres_are_known()
    {
        var summary = SpendSummary.Compute([
            new FuelEntry(Guid.NewGuid(), TestData.Car, new DateOnly(2026, 10, 10), 50m),
            new FuelEntry(Guid.NewGuid(), TestData.Car, new DateOnly(2026, 11, 10), 40m)], Today); // no price schedule
        var cut = Render<LitresCard>(p => p.Add(x => x.Summary, summary));
        var stats = StatValues(cut);
        Assert.Contains(stats, s => s.StartsWith("—") && s.Contains("This month"));
        Assert.Contains(stats, s => s.StartsWith("—") && s.Contains("Total litres"));
        Assert.DoesNotContain("estimated", cut.Markup);
    }

    [Fact]
    public void Litre_figures_are_plain_numbers_even_when_they_include_estimates()
    {
        var summary = SpendSummary.Compute([
            new FuelEntry(Guid.NewGuid(), TestData.Car, new DateOnly(2026, 10, 10), 50m),         // estimated
            new FuelEntry(Guid.NewGuid(), TestData.Car, new DateOnly(2026, 11, 10), 60m, 28m)],   // recorded
            Today, Budi);
        var cut = Render<LitresCard>(p => p.Add(x => x.Summary, summary));
        var stats = StatValues(cut);
        Assert.Contains(stats, s => s.StartsWith("53.1 L") && s.Contains("Total litres"));   // 25.13 estimated + 28 recorded, as a plain number
        Assert.DoesNotContain("≈", cut.Markup);
        Assert.Empty(cut.FindAll("p"));                   // minimalist: no explanatory text
    }

    [Fact]
    public void Litre_figures_stay_plain_when_every_litre_was_recorded()
    {
        var summary = SpendSummary.Compute([
            new FuelEntry(Guid.NewGuid(), TestData.Car, new DateOnly(2026, 10, 10), 60m, 30m),
            new FuelEntry(Guid.NewGuid(), TestData.Car, new DateOnly(2026, 11, 10), 60m, 28m)], Today, Budi);
        Assert.DoesNotContain("≈", Render<LitresCard>(p => p.Add(x => x.Summary, summary)).Markup);
    }
}

