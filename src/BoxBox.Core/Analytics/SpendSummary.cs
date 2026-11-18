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
    int EstimatedLitresEntries,
    IReadOnlyList<MonthSpend> Months,
    FuelEconomyResult Economy);

public static class SpendSummary
{
    public static SpendSummaryResult Compute(IEnumerable<FuelEntry> entries, DateOnly today, FuelPriceSchedule? prices = null)
    {
        prices ??= FuelPriceSchedule.Empty;
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

        // Average price uses only litres that were really recorded (receipts); estimated litres are
        // derived from a price, so using them here would be circular.
        var recorded = list.Where(e => e.Litres is > 0).ToList();
        decimal? avgPrice = recorded.Count > 0
            ? Math.Round(recorded.Sum(e => e.AmountPaid) / recorded.Sum(e => e.Litres!.Value), 3)
            : null;

        // Total litres = recorded + estimated (estimated from the amount paid and the scheduled price).
        var resolved = list.Select(prices.Resolve).Where(r => r.Litres is > 0).ToList();
        decimal? totalLitres = resolved.Count > 0 ? resolved.Sum(r => r.Litres!.Value) : null;
        var estimatedCount = resolved.Count(r => r.Estimated);

        return new SpendSummaryResult(
            list.Sum(e => e.AmountPaid), list.Count, thisMonth, lastMonth,
            avgMonthly, avgPrice, totalLitres, estimatedCount, months,
            FuelEconomy.Compute(list, prices));
    }
}
