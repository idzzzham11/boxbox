namespace BoxBox.Web.Components;

/// <summary>One point on a time-series chart.</summary>
public sealed record ChartPoint(DateOnly Date, double Value, string Tooltip);

/// <summary>
/// A "nice" y-axis: round start, end and step values (for example 0, 20, 40, 60) that cover the data.
/// Pure maths with no UI, so it is easy to unit test.
/// </summary>
public readonly record struct NiceScale(double Min, double Max, double Step)
{
    public static NiceScale For(double dataMin, double dataMax, bool zeroBased, int targetIntervals = 3)
    {
        if (zeroBased) dataMin = Math.Min(0, dataMin);
        if (dataMax <= dataMin) dataMax = dataMin + 1; // flat data still needs a range to draw

        var raw = (dataMax - dataMin) / targetIntervals;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var normalised = raw / magnitude;
        var step = (normalised <= 1 ? 1 : normalised <= 2 ? 2 : normalised <= 2.5 ? 2.5 : normalised <= 5 ? 5 : 10) * magnitude;

        var lo = Math.Floor(dataMin / step) * step;
        var hi = Math.Ceiling(dataMax / step) * step;
        if (hi <= lo) hi = lo + step;
        return new NiceScale(lo, hi, step);
    }

    /// <summary>The tick values from Min to Max inclusive.</summary>
    public IEnumerable<double> Ticks()
    {
        var count = (int)Math.Round((Max - Min) / Step);
        for (var i = 0; i <= count; i++) yield return Min + i * Step;
    }

    /// <summary>Maps a value to a vertical pixel position: Max at <paramref name="top"/>, Min at <paramref name="bottom"/>.</summary>
    public double Y(double value, double top, double bottom) => bottom - (value - Min) / (Max - Min) * (bottom - top);
}

public static class ChartMath
{
    /// <summary>
    /// Horizontal pixel position proportional to the date, so long gaps between fill-ups look like gaps.
    /// A single date (or all points on one day) is centred.
    /// </summary>
    public static double X(DateOnly date, DateOnly first, DateOnly last, double left, double right)
    {
        var span = last.DayNumber - first.DayNumber;
        return span <= 0 ? (left + right) / 2 : left + (double)(date.DayNumber - first.DayNumber) / span * (right - left);
    }
}
