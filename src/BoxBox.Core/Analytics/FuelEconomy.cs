using BoxBox.Core.Models;

namespace BoxBox.Core.Analytics;

/// <summary>Fuel used and distance driven between two consecutive fill-ups.</summary>
public sealed record EconomySegment(
    DateOnly Date, decimal Litres, int Km, decimal Cost,
    decimal LitresPer100Km, decimal KmPerLitre, decimal CostPerKm);

public sealed record FuelEconomyResult(
    int SegmentCount,
    int TotalKm,
    decimal? AverageLitresPer100Km,
    decimal? AverageKmPerLitre,
    decimal? AverageCostPerKm,
    EconomySegment? Last,
    IReadOnlyList<EconomySegment> Segments);

/// <summary>
/// Full-tank method using a trip meter that is reset at every fill-up.
/// <para>
/// Every fill-up is assumed to fill the tank. The litres put in at fill-up N therefore replace the
/// fuel burnt over the trip-meter distance recorded with it (the distance since fill-up N-1), so
/// each fill-up with litres and trip km is one measurement.
/// </para>
/// <para>
/// A measurement is only valid if the PREVIOUS fill-up is also known to be a measured full tank.
/// The first complete fill-up, and the first one after a record with litres or trip km missing
/// (e.g. old amount-only history), only sets the starting point. Nothing is ever estimated.
/// </para>
/// Litres may be estimated from the amount paid (see <see cref="FuelPriceSchedule"/>); recorded litres win.
/// Input order within one date is preserved (the sort is stable): list same-day fills in the
/// order they happened.
/// </summary>
public static class FuelEconomy
{
    public static FuelEconomyResult Compute(IEnumerable<FuelEntry> entries, FuelPriceSchedule? prices = null)
    {
        prices ??= FuelPriceSchedule.Empty;
        var segments = new List<EconomySegment>();
        var previousComplete = false;

        foreach (var e in entries.OrderBy(x => x.Date))
        {
            var resolved = prices.Resolve(e);
            if (resolved.Litres is not > 0 || e.TripKm is not > 0)
            {
                previousComplete = false; // unknown fuel or distance: chain broken
                continue;
            }

            if (previousComplete)
            {
                var litres = resolved.Litres.Value;
                var km = e.TripKm.Value;
                segments.Add(new EconomySegment(
                    e.Date, litres, km, e.AmountPaid,
                    Math.Round(litres / km * 100m, 2),
                    Math.Round(km / litres, 2),
                    Math.Round(e.AmountPaid / km, 3)));
            }
            previousComplete = true;
        }

        if (segments.Count == 0)
            return new FuelEconomyResult(0, 0, null, null, null, null, segments);

        var totalLitres = segments.Sum(s => s.Litres);
        var totalKm = segments.Sum(s => s.Km);
        var totalCost = segments.Sum(s => s.Cost);

        return new FuelEconomyResult(
            segments.Count, totalKm,
            Math.Round(totalLitres / totalKm * 100m, 2),
            Math.Round(totalKm / totalLitres, 2),
            Math.Round(totalCost / totalKm, 3),
            segments[^1], segments);
    }
}
