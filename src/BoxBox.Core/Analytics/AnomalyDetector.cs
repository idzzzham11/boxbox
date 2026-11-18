namespace BoxBox.Core.Analytics;

public enum AnomalyKind { None, HighConsumption, LowConsumption, PossibleMissedFillUp }

public sealed record SegmentAssessment(
    ConsumptionSegment Segment,
    AnomalyKind Kind,
    double? RobustZ,
    bool HadEnoughHistory);

public sealed record AnomalyOptions
{
    /// <summary>Segments needed before any statistical flag is raised.</summary>
    public int MinHistory { get; init; } = 5;
    /// <summary>How many preceding segments form the baseline.</summary>
    public int Window { get; init; } = 10;
    /// <summary>|modified z| above this is flagged (3.5 is the standard Iglewicz-Hoaglin cut-off).</summary>
    public double Threshold { get; init; } = 3.5;
    /// <summary>Floor for the scale as a fraction of the median, so a near-constant history
    /// (MAD ≈ 0) does not flag tiny wobbles.</summary>
    public double MinRelativeScale { get; init; } = 0.03;
    /// <summary>If known, a segment burning more litres than this (+5%) suggests a missed fill-up.</summary>
    public decimal? TankCapacityLitres { get; init; }
}

public static class AnomalyDetector
{
    /// <summary>
    /// Scores each segment against the segments that precede it (robust z-score on L/100km using
    /// median and MAD). Flags are hints (tyre pressure, AC, trip mix), not diagnoses.
    /// </summary>
    public static IReadOnlyList<SegmentAssessment> Assess(
        IReadOnlyList<ConsumptionSegment> segments, AnomalyOptions? options = null)
    {
        options ??= new AnomalyOptions();
        var results = new List<SegmentAssessment>(segments.Count);
        var baseline = new List<double>();

        foreach (var seg in segments)
        {
            // A fill bigger than the tank means a fill-up is missing; it must not poison the baseline.
            if (options.TankCapacityLitres is { } cap && seg.Litres > cap * 1.05m)
            {
                results.Add(new SegmentAssessment(seg, AnomalyKind.PossibleMissedFillUp, null, false));
                continue;
            }

            var window = baseline.Count > options.Window
                ? baseline.GetRange(baseline.Count - options.Window, options.Window)
                : baseline;

            if (window.Count < options.MinHistory)
            {
                results.Add(new SegmentAssessment(seg, AnomalyKind.None, null, false));
            }
            else
            {
                var median = Stats.Median(window);
                var scale = Math.Max(Stats.Mad(window) / 0.6745, median * options.MinRelativeScale);
                var z = (seg.LitresPer100Km - median) / scale;
                var kind = z > options.Threshold ? AnomalyKind.HighConsumption
                         : z < -options.Threshold ? AnomalyKind.LowConsumption
                         : AnomalyKind.None;
                results.Add(new SegmentAssessment(seg, kind, z, true));
            }

            baseline.Add(seg.LitresPer100Km);
        }

        return results;
    }
}
