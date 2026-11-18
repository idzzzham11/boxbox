using BoxBox.Core.Models;

namespace BoxBox.Core.Analytics;

public sealed record MonthSpend(int Year, int Month, decimal Total, int Count);

public sealed record SpendSummaryResult(
    decimal TotalSpent,
    int EntryCount,
    decimal ThisMonthSpent,
    decimal? LastMonthSpent,
    decimal? AverageMonthlySpent,
    decimal? AveragePricePerLitre,
    decimal? TotalLitres,
    IReadOnlyList<MonthSpend> Months);

public static class SpendSummary
{
    public static SpendSummaryResult Compute(IEnumerable<FuelEntry> entries, DateOnly today)
    {
        var list = entries.ToList();

        var months = list
            .GroupBy(e => (e.Date.Year, e.Date.Month))
            .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
            .Select(g => new MonthSpend(g.Key.Year, g.Key.Month, g.Sum(e => e.AmountPaid), g.Count()))
            .ToList();

        var thisMonth = months.FirstOrDefault(m => m.Year == today.Year && m.Month == today.Month)?.Total ?? 0m;
        var prev = today.AddMonths(-1);
        var lastMonth = months.FirstOrDefault(m => m.Year == prev.Year && m.Month == prev.Month)?.Total;

        // Average over completed months only, so a half-finished month doesn't drag it down.
        var completed = months.Where(m => (m.Year, m.Month) != (today.Year, today.Month)).ToList();
        decimal? avgMonthly = completed.Count > 0 ? Math.Round(completed.Average(m => m.Total), 2) : null;

        // Price per litre / total litres use only entries that recorded litres.
        var withLitres = list.Where(e => e.Litres is > 0).ToList();
        decimal? avgPrice = null, totalLitres = null;
        if (withLitres.Count > 0)
        {
            totalLitres = withLitres.Sum(e => e.Litres!.Value);
            avgPrice = Math.Round(withLitres.Sum(e => e.AmountPaid) / totalLitres.Value, 3);
        }

        return new SpendSummaryResult(
            list.Sum(e => e.AmountPaid), list.Count, thisMonth, lastMonth,
            avgMonthly, avgPrice, totalLitres, months);
    }
}
