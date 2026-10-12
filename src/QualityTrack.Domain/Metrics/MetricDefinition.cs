namespace QualityTrack.Domain.Metrics;

public enum MetricDirection
{
    /// <summary>Changes are neither good nor bad (e.g. lines of code).</summary>
    Neutral = 0,
    HigherIsBetter = 1,
    LowerIsBetter = 2
}

public enum MetricUnit
{
    Count = 0,
    Percent = 1,
    Minutes = 2,
    Seconds = 3,
    /// <summary>1 = A (best) … 5 = E (worst).</summary>
    Rating = 4,
    /// <summary>1 = passed, 0 = failed.</summary>
    Flag = 5
}

public sealed record MetricDefinition(string Key, string Name, MetricUnit Unit, MetricDirection Direction, string Group);
