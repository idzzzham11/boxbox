namespace BoxBox.Core.Analytics;

internal static class Stats
{
    public static double Median(IReadOnlyList<double> values) => Percentile(values, 0.5);

    /// <summary>Linear-interpolated percentile (p in 0..1).</summary>
    public static double Percentile(IReadOnlyList<double> values, double p)
    {
        if (values.Count == 0) throw new ArgumentException("No values.", nameof(values));
        var sorted = values.OrderBy(v => v).ToArray();
        var pos = p * (sorted.Length - 1);
        var lo = (int)Math.Floor(pos);
        var hi = (int)Math.Ceiling(pos);
        return sorted[lo] + (sorted[hi] - sorted[lo]) * (pos - lo);
    }

    /// <summary>Median absolute deviation around the median.</summary>
    public static double Mad(IReadOnlyList<double> values)
    {
        var m = Median(values);
        return Median(values.Select(v => Math.Abs(v - m)).ToArray());
    }
}
