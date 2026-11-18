using System.Net;
using System.Text;
using System.Text.Json;
using BoxBox.Core.Analytics;
using BoxBox.Core.Contracts;
using BoxBox.Core.Models;
using BoxBox.Web.Components;
using BoxBox.Web.Pages;
using BoxBox.Web.Services;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace BoxBox.Tests;

/// <summary>
/// Component tests with bUnit: each test renders a Blazor component in memory (no browser) and asserts on the
/// HTML it produces. The HTTP calls are answered by a fake handler that returns JSON built from the real
/// BoxBox.Core types, so these tests also prove that the server's JSON shapes deserialise in the web client.
/// </summary>
public class BlazorComponentTests : BunitContext
{
    private static readonly DateOnly Today = new(2026, 11, 18);
    private static readonly FuelPriceSchedule Budi = new([new PricePeriod(new DateOnly(2025, 9, 30), 1.99m)]);

    // Invented history: 12 fill-ups, some with trip km so fuel economy exists.
    private static List<FuelEntry> Sample() =>
    [
        E("2026-09-03", 25), E("2026-09-08", 48.5m), E("2026-09-13", 52, trip: 300), E("2026-09-14", 18),
        E("2026-09-20", 55.2m), E("2026-09-28", 40), E("2026-10-07", 61.3m), E("2026-10-15", 35),
        E("2026-10-27", 50.75m, trip: 380), E("2026-11-02", 12), E("2026-11-08", 58, trip: 350), E("2026-11-15", 27.4m, trip: 160),
    ];

    private static FuelEntry E(string date, decimal amount, int? trip = null) =>
        new(Guid.NewGuid(), TestData.Car, DateOnly.Parse(date), amount, null, trip);

    private sealed class FakeHandler(Func<HttpRequestMessage, (HttpStatusCode, string)> respond) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, string? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            Requests.Add((request.Method, request.RequestUri!.AbsolutePath, body));
            var (status, json) = respond(request);
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    private static string Json(object o) => JsonSerializer.Serialize(o, JsonSerializerOptions.Web);

    private FakeHandler Backend(List<FuelEntry>? entries = null, Func<HttpRequestMessage, (HttpStatusCode, string)?>? custom = null)
    {
        entries ??= Sample();
        var handler = new FakeHandler(req =>
        {
            if (custom?.Invoke(req) is { } c) return c;
            return req.RequestUri!.AbsolutePath switch
            {
                "/api/summary" => (HttpStatusCode.OK, Json(SpendSummary.Compute(entries, Today, Budi))),
                "/api/forecast" => (HttpStatusCode.OK, Json(SpendForecaster.Compute(entries, Today, Budi))),
                "/api/entries" when req.Method == HttpMethod.Get => (HttpStatusCode.OK, Json(
                    entries.OrderByDescending(e => e.Date).Select(e =>
                    {
                        var r = Budi.Resolve(e);
                        return new EntryDto(e.Id, e.Date, e.AmountPaid, r.Litres, r.PricePerLitre, e.TripKm, r.Estimated);
                    }))),
                _ => (HttpStatusCode.NotFound, "{}"),
            };
        });
        Services.AddScoped(_ => new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") });
        Services.AddScoped<BoxBoxApi>();
        return handler;
    }

    [Fact]
    public void Home_renders_all_cards_from_the_servers_json()
    {
        Backend();
        var cut = Render<Home>();
        cut.WaitForState(() => cut.FindAll("section.card").Count >= 7, TimeSpan.FromSeconds(5));

        var text = cut.Markup;
        Assert.Contains("<h2>Price</h2>", text);
        Assert.Contains("<h2>Litres</h2>", text);
        Assert.Contains("<h2>Forecast</h2>", text);
        Assert.Contains("<h2>Spending by month</h2>", text);
        Assert.Contains("<h2>History</h2>", text);
        Assert.Contains("12 fill-ups", text);                  // total card
        Assert.Contains("12 estimated", text);                 // litres are all estimated in this sample
        Assert.Contains("Low confidence", text.Replace("\n", " ").Replace("  ", " "));
    }

    [Fact]
    public void Price_card_shows_formatted_money()
    {
        var summary = SpendSummary.Compute(Sample(), Today, Budi);
        var cut = Render<PriceCard>(p => p.Add(x => x.Summary, summary));
        Assert.Contains("RM " + summary.TotalSpent.ToString("N2", System.Globalization.CultureInfo.InvariantCulture), cut.Markup);
        Assert.Contains("This month", cut.Markup);
        Assert.Equal(4, cut.FindAll(".stat").Count);
    }

    [Fact]
    public void Litres_card_shows_economy_when_trip_km_exist()
    {
        var summary = SpendSummary.Compute(Sample(), Today, Budi);
        Assert.True(summary.Economy.SegmentCount > 0);
        var cut = Render<LitresCard>(p => p.Add(x => x.Summary, summary));
        Assert.Contains("L/100km", cut.Markup);
        Assert.Contains("Avg cost per km", cut.Markup);
    }

    [Fact]
    public void Litres_card_explains_how_to_get_economy_when_there_is_none()
    {
        var summary = SpendSummary.Compute([E("2026-11-01", 50)], Today, Budi);
        var cut = Render<LitresCard>(p => p.Add(x => x.Summary, summary));
        Assert.DoesNotContain("L/100km", cut.Markup);
        Assert.Contains("trip-meter", cut.Markup);
    }

    [Fact]
    public void Forecast_card_explains_why_there_is_no_forecast()
    {
        var f = SpendForecaster.Compute(Sample().Take(3), Today, Budi);
        var cut = Render<ForecastCard>(p => p.Add(x => x.Forecast, f));
        Assert.Contains("at least 6", cut.Markup);
        Assert.Empty(cut.FindAll(".stat"));
    }

    [Fact]
    public void Forecast_card_shows_ranges_and_the_derived_litres_warning()
    {
        var f = SpendForecaster.Compute(Sample(), Today, Budi);
        var cut = Render<ForecastCard>(p => p.Add(x => x.Forecast, f));
        Assert.Contains("Next fill-up", cut.Markup);
        Assert.Contains("≈", cut.Markup);
        Assert.Contains("add no new information", cut.Markup);
    }

    [Fact]
    public void History_marks_estimated_litres_and_shows_trip_km()
    {
        var entries = Sample().OrderByDescending(e => e.Date).Select(e =>
        {
            var r = Budi.Resolve(e);
            return new EntryDto(e.Id, e.Date, e.AmountPaid, r.Litres, r.PricePerLitre, e.TripKm, r.Estimated);
        }).ToList();
        JSInterop.Mode = JSRuntimeMode.Loose;
        var cut = Render<HistoryList>(p => p.Add(x => x.Entries, entries));
        Assert.Equal(12, cut.FindAll("li").Count);
        Assert.Contains("≈ ", cut.Markup);
        Assert.Contains(" est.", cut.Markup);
        Assert.Contains("160 km", cut.Markup);
    }

    [Fact]
    public async Task History_delete_asks_first_and_only_calls_back_when_confirmed()
    {
        var entry = new EntryDto(Guid.NewGuid(), Today, 40m, null, null, null, false);
        Guid? deleted = null;
        JSInterop.Setup<bool>("confirm", _ => true).SetResult(false);
        var cut = Render<HistoryList>(p => p
            .Add(x => x.Entries, [entry])
            .Add(x => x.OnDelete, EventCallback.Factory.Create<Guid>(this, id => deleted = id)));

        await cut.Find("button.link").ClickAsync();
        Assert.Null(deleted); // user said no

        JSInterop.Setup<bool>("confirm", _ => true).SetResult(true);
        await cut.Find("button.link").ClickAsync();
        Assert.Equal(entry.Id, deleted);
    }

    [Fact]
    public async Task Add_form_blocks_an_empty_submit_and_sends_nothing()
    {
        var handler = Backend();
        var cut = Render<AddEntryForm>();
        await cut.Find("form").SubmitAsync();
        Assert.Contains("Enter the amount paid", cut.Markup);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Add_form_posts_the_entry_and_notifies_the_page()
    {
        var handler = Backend(custom: r => r.Method == HttpMethod.Post
            ? (HttpStatusCode.Created, "{}") : null);
        var saved = 0;
        var cut = Render<AddEntryForm>(p => p.Add(x => x.OnSaved, EventCallback.Factory.Create(this, () => saved++)));

        // Re-query after every change: a change re-renders the form, which makes earlier element references stale.
        cut.FindAll("input")[0].Change("59.70");   // paid
        cut.FindAll("input")[2].Change("400");     // trip km
        await cut.Find("form").SubmitAsync();

        var post = Assert.Single(handler.Requests, r => r.Method == HttpMethod.Post);
        Assert.Equal("/api/entries", post.Path);
        using var doc = JsonDocument.Parse(post.Body!);
        Assert.Equal(59.70m, doc.RootElement.GetProperty("amountPaid").GetDecimal());
        Assert.Equal(400, doc.RootElement.GetProperty("tripKm").GetInt32());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("litres").ValueKind); // left empty -> estimated by server
        Assert.Equal(1, saved);
    }

    [Fact]
    public async Task Add_form_shows_the_servers_validation_errors()
    {
        Backend(custom: r => r.Method == HttpMethod.Post
            ? (HttpStatusCode.BadRequest, Json(new { errors = new[] { "Trip km looks too high for one tank (max 2000)." } })) : null);
        var cut = Render<AddEntryForm>();
        cut.FindAll("input")[0].Change("50");
        await cut.Find("form").SubmitAsync();
        Assert.Contains("Trip km looks too high", cut.Markup);
    }

    [Fact]
    public void Home_shows_a_helpful_message_when_the_server_is_unreachable()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("down"));
        Services.AddScoped(_ => new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") });
        Services.AddScoped<BoxBoxApi>();
        var cut = Render<Home>();
        cut.WaitForState(() => cut.Markup.Contains("Could not reach the server"), TimeSpan.FromSeconds(5));
        Assert.Contains("Try again", cut.Markup);
    }

    [Fact]
    public void Month_bars_show_newest_first_and_scale_to_the_biggest_month()
    {
        var months = new List<MonthSpend> { new(2026, 9, 100m, 2), new(2026, 10, 200m, 3), new(2026, 11, 50m, 1) };
        var cut = Render<MonthBars>(p => p.Add(x => x.Data, months));
        var rows = cut.FindAll(".bar");
        Assert.Equal(3, rows.Count);
        Assert.Contains("Nov 26", rows[0].TextContent);
        Assert.Contains("width:100.0%", rows[1].InnerHtml);   // October is the max
        Assert.Contains("width:25.0%", rows[0].InnerHtml);    // November = 50/200
    }
}
