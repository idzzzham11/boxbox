using BoxBox.Core.Models;

namespace BoxBox.Core.Analytics;

public static class ConsumptionCalculator
{
    /// <summary>
    /// Full-tank method. The first full fill-up is only a baseline. Every later full fill-up closes
    /// a segment whose litres are the sum of all fill-ups since the previous full one (inclusive of
    /// this one, exclusive of the baseline). Partial fills before the first full one are ignored.
    /// Fill-ups are ordered by odometer, which is authoritative.
    /// </summary>
    public static IReadOnlyList<ConsumptionSegment> Segments(IEnumerable<FillUp> fillUps)
    {
        var ordered = fillUps.OrderBy(f => f.OdometerKm).ThenBy(f => f.Date).ToList();
        var segments = new List<ConsumptionSegment>();

        FillUp? baseline = null;
        decimal litres = 0, cost = 0;
        int count = 0;

        foreach (var f in ordered)
        {
            if (baseline is null)
            {
                if (f.IsFullTank) baseline = f;
                continue;
            }

            litres += f.Litres;
            cost += f.TotalCost;
            count++;

            if (!f.IsFullTank) continue;

            if (f.OdometerKm > baseline.OdometerKm)
            {
                segments.Add(new ConsumptionSegment(
                    baseline.Date, f.Date, baseline.OdometerKm, f.OdometerKm, litres, cost, count));
            }

            baseline = f;
            litres = 0; cost = 0; count = 0;
        }

        return segments;
    }
}
