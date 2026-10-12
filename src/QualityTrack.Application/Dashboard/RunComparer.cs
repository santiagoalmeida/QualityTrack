using QualityTrack.Domain.Entities;
using QualityTrack.Domain.Metrics;
using QualityTrack.Domain.Progress;

namespace QualityTrack.Application.Dashboard;

internal static class RunComparer
{
    /// <summary>
    /// Metrics of <paramref name="current"/> per project, each next to its value in <paramref name="previous"/>
    /// and to its goal. Projects that reported nothing in the run are left out.
    /// </summary>
    public static List<ProjectComparison> Compare(
        Solution solution,
        Run current,
        Run? previous,
        string[]? onlyMetrics,
        IReadOnlySet<(Guid ProjectId, string MetricKey)>? withFindings = null)
    {
        var before = previous?.Measurements.ToDictionary(m => (m.ProjectId, m.MetricKey), m => m.Value) ?? [];
        var goals = solution.Goals.ToDictionary(g => (g.ProjectId, g.MetricKey));

        return solution.Projects
            .OrderBy(c => c.Name)
            .Select(project =>
            {
                var metrics = current.Measurements
                    .Where(m => m.ProjectId == project.Id && (onlyMetrics is null || onlyMetrics.Contains(m.MetricKey)))
                    .OrderBy(m => MetricCatalog.Order(m.MetricKey))
                    .ThenBy(m => m.MetricKey)
                    .Select(m =>
                    {
                        var definition = MetricCatalog.Get(m.MetricKey);
                        double? previousValue = before.TryGetValue((project.Id, m.MetricKey), out var value) ? value : null;
                        var goal = goals.TryGetValue((project.Id, m.MetricKey), out var g)
                            ? new GoalStatus(
                                g.Baseline,
                                g.Target,
                                ProgressCalculator.GoalProgress(g.Baseline, g.Target, m.Value, definition.Direction),
                                ProgressCalculator.IsMet(g.Target, m.Value, definition.Direction))
                            : null;
                        var trend = previousValue is { } p ? ProgressCalculator.Judge(p, m.Value, definition.Direction) : Trend.Unchanged;
                        return new MetricComparison(definition, m.Value, previousValue, trend, goal, withFindings?.Contains((project.Id, m.MetricKey)) == true);
                    })
                    .ToList();
                return new ProjectComparison(project.Id, project.Key, project.Name, metrics);
            })
            .Where(c => c.Metrics.Count > 0)
            .ToList();
    }
}
