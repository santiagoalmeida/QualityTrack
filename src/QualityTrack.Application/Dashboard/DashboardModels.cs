using QualityTrack.Domain.Metrics;
using QualityTrack.Domain.Progress;

namespace QualityTrack.Application.Dashboard;

public sealed record RunInfo(Guid Id, string Branch, string? CommitSha, bool IsDirty, string? Source, DateTime ExecutedAt);

/// <param name="Progress">Fraction (0..1) of the way from baseline to target.</param>
public sealed record GoalStatus(double Baseline, double Target, double Progress, bool Met);

public sealed record MetricComparison(MetricDefinition Metric, double Current, double? Previous, Trend Trend, GoalStatus? Goal)
{
    public double? Delta => Previous is { } previous ? Current - previous : null;
}

public sealed record ComponentComparison(Guid ComponentId, string Key, string Name, IReadOnlyList<MetricComparison> Metrics);

public sealed record BranchInfo(string Name, int RunCount, DateTime LastRunAt);

public sealed record RunHistoryItem(RunInfo Run, double? Progress);

public sealed record TrendPoint(DateTime At, string Label, double Value);

public sealed record TrendSeries(string ComponentName, MetricDefinition Metric, IReadOnlyList<TrendPoint> Points);

/// <param name="Progress">Overall progress toward goals in the last run (0..1).</param>
/// <param name="PreviousProgress">Same figure for the run before it on the same branch.</param>
public sealed record ProjectSummary(
    string Key,
    string Name,
    int RunCount,
    RunInfo? LastRun,
    double? Progress,
    double? PreviousProgress,
    IReadOnlyList<ComponentComparison> Components);

public sealed record ProjectDashboard(
    string Key,
    string Name,
    IReadOnlyList<BranchInfo> Branches,
    string? Branch,
    RunInfo? Run,
    RunInfo? PreviousRun,
    double? Progress,
    double? PreviousProgress,
    IReadOnlyList<ComponentComparison> Components,
    IReadOnlyList<RunHistoryItem> History,
    IReadOnlyList<TrendSeries> Trends);
