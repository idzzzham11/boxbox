namespace BoxBox.Core.Models;

/// <summary>
/// A simple spend record: what you paid at the pump and, when known, how many litres you got.
/// Litres are optional so historic records (amount only) can be imported.
/// </summary>
public sealed record FuelEntry(
    Guid Id,
    Guid VehicleId,
    DateOnly Date,
    decimal AmountPaid,
    decimal? Litres = null)
{
    /// <summary>RM per litre, or null when litres are unknown.</summary>
    public decimal? PricePerLitre =>
        Litres is > 0 ? Math.Round(AmountPaid / Litres.Value, 3) : null;
}
