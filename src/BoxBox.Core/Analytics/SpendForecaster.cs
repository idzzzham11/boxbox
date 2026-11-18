using BoxBox.Core.Models;

namespace BoxBox.Core.Analytics;

/// <summary>One forecast value with the evidence behind it.</summary>
/// <param name="Model">Which candidate model won the backtest.</param>
/// <param name="Predicted">Point forecast.</param>
/// <param name="Low">Predicted minus the model's typical backtest error (never below zero).</param>
/// <param name="High">Predicted plus the model's typical backtest error.</param>
/// <param name="ModelMae">Mean absolute error of the winning model in the rolling backtest.</param>
/// <param name="NaiveMae">Mean absolute error of "same as last time" over the same backtest.</param>
/// <param name="BeatsNaive">True only when the winner is strictly better than the naive guess.</param>
/// <param name="Evaluations">How many one-step-ahead predictions the backtest scored.</param>
public sealed record ForecastSeries(
    string Model, double Predicted, double Low, double High,
    double ModelMae, double NaiveMae, bool BeatsNaive, int Evaluations);

public sealed record ForecastResult(
    bool Available,
    string? Reason,
    int FillUps,
    string Confidence,
    DateOnly? NextDate,
    ForecastSeries? IntervalDays,
    ForecastSeries? Amount,
    ForecastSeries? Litres,
    decimal? ProjectedMonthSpend,
    bool LitresDerivedFromPrice);

/// <summary>
/// Forecasts the next fill-up (when, how much RM, how many litres) and this month's total spend from the
/// owner's own history.
/// <para>
/// With one car there are only a handful of data points, so this deliberately uses small, transparent models
/// (last value, mean, median of the last 6, exponential smoothing) rather than anything that needs hundreds
/// of rows. The "learning" is model selection: for each signal every candidate is scored by a rolling-origin
/// backtest (train on the first k values, predict value k, repeat) and the lowest-error one is used. The
/// naive "same as last time" guess is always scored too, so the result states honestly whether the chosen
/// model actually beats it.
/// </para>
/// Pump prices are not forecast: the BUDI95 price is set by government policy, not by a pattern in your data.
/// </summary>
public static class SpendForecaster
{
    public const int MinFillUps = 6;
    private const int MinTrain = 3;

    private sealed record Candidate(string Name, Func<IReadOnlyList<double>, double> Predict);

    // Order matters: on an exact tie the earlier (simpler) model wins.
    private static readonly Candidate[] Candidates =
    [
        new("last value", h => h[^1]),
        new("average of all", h => h.Average()),
        new("median of last 6", h => Stats.Median(h.Skip(Math.Max(0, h.Count - 6)).ToArray())),
        new("exponential smoothing", h =>
        {
            const double alpha = 0.4;
            var level = h[0];
            for (var i = 1; i < h.Count; i++) level = alpha * h[i] + (1 - alpha) * level;
            return level;
        }),
    ];

    public static ForecastResult Compute(IEnumerable<FuelEntry> entries, DateOnly today, FuelPriceSchedule? prices = null)
    {
        prices ??= FuelPriceSchedule.Empty;
        var list = entries.OrderBy(e => e.Date).ToList();

        if (list.Count < MinFillUps)
            return new ForecastResult(false,
                $"Needs at least {MinFillUps} fill-ups to forecast (you have {list.Count}).",
                list.Count, "None", null, null, null, null, null, false);

        var intervals = list.Zip(list.Skip(1), (a, b) => (double)(b.Date.DayNumber - a.Date.DayNumber)).ToArray();
        var amounts = list.Select(e => (double)e.AmountPaid).ToArray();

        var resolved = list.Select(prices.Resolve).Where(r => r.Litres is > 0).ToList();
        var litres = resolved.Count >= MinFillUps ? resolved.Select(r => (double)r.Litres!.Value).ToArray() : null;
        var derived = resolved.Count > 0 && resolved.All(r => r.Estimated);

        var interval = Forecast(intervals, minValue: 1);
        var amount = Forecast(amounts, minValue: 0);
        var litre = litres is null ? null : Forecast(litres, minValue: 0);

        var last = list[^1].Date;
        var nextDate = Max(last.AddDays(Math.Max(1, (int)Math.Round(interval.Predicted))), today);

        return new ForecastResult(true, null, list.Count, Confidence(list.Count),
            nextDate, interval, amount, litre,
            ProjectMonth(list, today, interval.Predicted, amount.Predicted), derived);
    }

    private static string Confidence(int fillUps) => fillUps switch
    {
        < 8 => "Very low",
        < 20 => "Low",
        < 40 => "Moderate",
        _ => "Good",
    };

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;

    /// <summary>
    /// Spent so far this month plus the fill-ups expected before month end, stepping forward from the last
    /// fill-up by the predicted interval and adding the predicted amount each time.
    /// </summary>
    private static decimal ProjectMonth(List<FuelEntry> list, DateOnly today, double intervalDays, double amount)
    {
        var spent = list.Where(e => e.Date.Year == today.Year && e.Date.Month == today.Month).Sum(e => e.AmountPaid);
        var monthEnd = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
        var step = Math.Max(1, (int)Math.Round(intervalDays));

        var date = Max(list[^1].Date.AddDays(step), today);
        while (date <= monthEnd)
        {
            spent += Math.Round((decimal)amount, 2);
            date = date.AddDays(step);
        }
        return spent;
    }

    /// <summary>Scores every candidate with a rolling-origin backtest and forecasts with the best.</summary>
    internal static ForecastSeries Forecast(IReadOnlyList<double> series, double minValue)
    {
        var scores = Candidates.Select(c =>
        {
            var errors = new List<double>();
            for (var k = MinTrain; k < series.Count; k++)
                errors.Add(Math.Abs(c.Predict(series.Take(k).ToArray()) - series[k]));
            return (Candidate: c, Mae: errors.Count > 0 ? errors.Average() : double.NaN, Count: errors.Count);
        }).ToList();

        var naive = scores[0];
        // OrderBy is stable, so exact ties keep the simpler (earlier) model.
        var best = scores.OrderBy(s => s.Mae).First();

        var predicted = Math.Max(minValue, best.Candidate.Predict(series.ToArray()));
        return new ForecastSeries(
            best.Candidate.Name,
            predicted,
            Math.Max(minValue, predicted - best.Mae),
            predicted + best.Mae,
            best.Mae, naive.Mae,
            best.Mae < naive.Mae - 1e-9,
            best.Count);
    }
}
