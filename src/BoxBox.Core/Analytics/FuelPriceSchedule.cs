using BoxBox.Core.Models;

namespace BoxBox.Core.Analytics;

/// <summary>A pump price (RM per litre) that applies from <see cref="From"/> until the next period.</summary>
public sealed record PricePeriod(DateOnly From, decimal PerLitre);

/// <summary>Litres for one entry: as recorded, or estimated from the amount paid and the price on that date.</summary>
public sealed record ResolvedLitres(decimal? Litres, bool Estimated, decimal? PricePerLitre);

/// <summary>
/// The pump price in force on a date, e.g. the subsidised BUDI95 RON95 price. Used to estimate litres
/// for entries where only the amount paid is known (litres = amount / price).
/// An empty schedule estimates nothing.
/// </summary>
public sealed class FuelPriceSchedule
{
    public static readonly FuelPriceSchedule Empty = new([]);

    private readonly List<PricePeriod> _periods;

    public FuelPriceSchedule(IEnumerable<PricePeriod> periods) =>
        _periods = periods.Where(p => p.PerLitre > 0).OrderBy(p => p.From).ToList();

    /// <summary>Price on the date, or null when the date is before the first period.</summary>
    public decimal? PriceOn(DateOnly date) =>
        _periods.LastOrDefault(p => p.From <= date)?.PerLitre;

    /// <summary>
    /// Recorded litres always win. Otherwise litres are estimated (2 d.p., like a pump display) from the
    /// amount and the scheduled price; with no price known for that date there is no estimate.
    /// </summary>
    public ResolvedLitres Resolve(FuelEntry e)
    {
        if (e.Litres is > 0)
            return new ResolvedLitres(e.Litres, false, e.PricePerLitre);

        var price = PriceOn(e.Date);
        if (price is null || e.AmountPaid <= 0)
            return new ResolvedLitres(null, false, null);

        return new ResolvedLitres(Math.Round(e.AmountPaid / price.Value, 2), true, price);
    }
}
