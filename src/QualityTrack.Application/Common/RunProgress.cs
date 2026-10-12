using QualityTrack.Domain.Entities;
using QualityTrack.Domain.Metrics;
using QualityTrack.Domain.Progress;

namespace QualityTrack.Application.Common;

internal static class RunProgress
{
    /// <summary>Overall progress of a run toward the project's goals; goals whose metric the run did not report are skipped.</summary>
    public static double? Compute(IEnumerable<Goal> goals, IEnumerable<Measurement> measurements)
    {
        var values = measurements.ToDictionary(m => (m.ComponentId, m.MetricKey), m => m.Value);
        var progresses = new List<double>();
        foreach (var goal in goals)
        {
            if (values.TryGetValue((goal.ComponentId, goal.MetricKey), out var current))
                progresses.Add(ProgressCalculator.GoalProgress(goal.Baseline, goal.Target, current, MetricCatalog.Get(goal.MetricKey).Direction));
        }

        return ProgressCalculator.Overall(progresses);
    }
}
