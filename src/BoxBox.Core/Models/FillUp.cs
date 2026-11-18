namespace BoxBox.Core.Models;

/// <summary>
/// One refuelling event. Price per litre comes from the receipt, not from any public price feed.
/// Dates are local calendar dates (Asia/Kuala_Lumpur); there is no time of day.
/// </summary>
public sealed record FillUp(
    Guid Id,
    Guid VehicleId,
    DateOnly Date,
    int OdometerKm,
    decimal Litres,
    decimal PricePerLitre,
    bool IsFullTank = true)
{
    public decimal TotalCost => Math.Round(Litres * PricePerLitre, 2);
}
