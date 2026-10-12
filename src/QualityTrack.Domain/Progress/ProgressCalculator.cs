using QualityTrack.Domain.Metrics;

namespace QualityTrack.Domain.Progress;

public static class ProgressCalculator
{
    /// <summary>
    /// Fraction (0..1) of the way from <paramref name="baseline"/> to <paramref name="target"/> covered by
    /// <paramref name="current"/>. A metric that already meets its target counts as 1; one that got worse
    /// than the baseline counts as 0.
    /// </summary>
    public static double GoalProgress(double baseline, double target, double current, MetricDirection direction)
    {
        if (IsMet(target, current, direction))
            return 1;

        var span = baseline - target;
        if (span == 0)
            return 0;

        // Compared rather than clamped: a metric still at its baseline divides to -0, which Clamp would keep.
        var progress = (baseline - current) / span;
        return progress <= 0 ? 0 : Math.Min(progress, 1);
    }

    public static bool IsMet(double target, double current, MetricDirection direction) => direction switch
    {
        MetricDirection.HigherIsBetter => current >= target,
        MetricDirection.LowerIsBetter => current <= target,
        _ => current == target
    };

    /// <summary>Mean progress across goals, or null when there is nothing to average.</summary>
    public static double? Overall(IEnumerable<double> goalProgresses)
    {
        var values = goalProgresses.ToList();
        return values.Count == 0 ? null : values.Average();
    }

    /// <summary>Whether going from <paramref name="previous"/> to <paramref name="current"/> is good, bad or indifferent.</summary>
    public static Trend Judge(double previous, double current, MetricDirection direction)
    {
        if (current == previous || direction == MetricDirection.Neutral)
            return Trend.Unchanged;

        var improved = direction == MetricDirection.HigherIsBetter ? current > previous : current < previous;
        return improved ? Trend.Improved : Trend.Worsened;
    }
}

public enum Trend
{
    Unchanged = 0,
    Improved = 1,
    Worsened = 2
}
