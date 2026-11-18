namespace BoxBox.Core.Models;

/// <summary>
/// A spend record: what you paid at the pump and, when known, how many litres you got.
/// Litres are optional so historic records (amount only) can be imported.
/// <para>
/// <paramref name="TripKm"/> is the distance on the trip meter since the PREVIOUS fill-up
/// (the owner resets the trip meter after every fill). Every fill-up is assumed to fill the
/// tank to the pump's automatic cut-off.
/// </para>
/// </summary>
public sealed record FuelEntry(
    Guid Id,
    Guid VehicleId,
    DateOnly Date,
    decimal AmountPaid,
    decimal? Litres = null,
    int? TripKm = null)
{
    /// <summary>RM per litre, or null when litres are unknown.</summary>
    public decimal? PricePerLitre =>
        Litres is > 0 ? Math.Round(AmountPaid / Litres.Value, 3) : null;
}
