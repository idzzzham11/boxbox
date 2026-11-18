using BoxBox.Core.Models;

namespace BoxBox.Core.Analytics;

public sealed record FillUpForecast(
    DateOnly Expected,
    DateOnly Earliest,
    DateOnly Latest,
    double AverageDailyKm,
    int SegmentsUsed);

public static class FillUpForecaster
{
    /// <summary>
    /// Average km per day over the whole history (total distance / total days between the first and
    /// last fill-up). Null if there are fewer than two fill-ups or all share one date.
    /// </summary>
    public static double? AverageDailyKm(IEnumerable<FillUp> fillUps)
    {
        var ordered = fillUps.OrderBy(f => f.OdometerKm).ToList();
        if (ordered.Count < 2) return null;
        var days = ordered[^1].Date.DayNumber - ordered[0].Date.DayNumber;
        var km = ordered[^1].OdometerKm - ordered[0].OdometerKm;
        return days <= 0 || km <= 0 ? null : (double)km / days;
    }

    /// <summary>
    /// Next full-tank fill-up date: the owner's typical km per tank (median segment distance) divided by
    /// average daily km, counted from the most recent full fill-up. The range uses the 25th/75th
    /// percentile of the owner's own km per tank. Needs at least <paramref name="minSegments"/> segments.
    /// </summary>
    public static FillUpForecast? Forecast(IReadOnlyCollection<FillUp> fillUps, int minSegments = 3)
    {
        var segments = ConsumptionCalculator.Segments(fillUps);
        if (segments.Count < minSegments) return null;
        if (AverageDailyKm(fillUps) is not { } daily) return null;

        var lastFull = fillUps.Where(f => f.IsFullTank).OrderBy(f => f.OdometerKm).Last();
        var km = segments.Select(s => (double)s.DistanceKm).ToArray();

        DateOnly At(double distance) => lastFull.Date.AddDays((int)Math.Round(distance / daily));

        return new FillUpForecast(
            At(Stats.Median(km)),
            At(Stats.Percentile(km, 0.25)),
            At(Stats.Percentile(km, 0.75)),
            daily,
            segments.Count);
    }
}
