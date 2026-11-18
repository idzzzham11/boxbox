namespace BoxBox.Core.Analytics;

/// <summary>
/// The stretch between two consecutive full-tank fill-ups (full-tank method).
/// Partial fills inside the stretch are summed into <see cref="Litres"/>.
/// </summary>
public sealed record ConsumptionSegment(
    DateOnly StartDate,
    DateOnly EndDate,
    int StartOdometerKm,
    int EndOdometerKm,
    decimal Litres,
    decimal Cost,
    int FillUpCount)
{
    public int DistanceKm => EndOdometerKm - StartOdometerKm;
    public int Days => Math.Max(1, EndDate.DayNumber - StartDate.DayNumber);
    public double LitresPer100Km => (double)Litres / DistanceKm * 100.0;
    public double KmPerLitre => DistanceKm / (double)Litres;
    public decimal CostPerKm => Cost / DistanceKm;
}
