using Microsoft.EntityFrameworkCore;
using QualityTrack.Application.Abstractions;
using QualityTrack.Application.Common;
using QualityTrack.Domain.Entities;
using QualityTrack.Domain.Metrics;
using QualityTrack.Domain.Progress;

namespace QualityTrack.Application.Dashboard;

public class DashboardService(IAppDbContext db)
{
    private const int HistorySize = 30;

    private static readonly string[] HighlightMetrics =
        [MetricCatalog.Coverage, MetricCatalog.Bugs, MetricCatalog.Vulnerabilities, MetricCatalog.TestsFailed];

    private static readonly string[] TrendMetrics =
        [MetricCatalog.Coverage, MetricCatalog.Bugs, MetricCatalog.Vulnerabilities, MetricCatalog.CodeSmells];

    public async Task<IReadOnlyList<ProjectSummary>> GetOverviewAsync(Guid organizationId, CancellationToken ct = default)
    {
        var projects = await db.Projects
            .AsNoTracking()
            .Include(p => p.Components)
            .Include(p => p.Goals)
            .Where(p => p.OrganizationId == organizationId)
            .OrderBy(p => p.Name)
            .ToListAsync(ct);

        var summaries = new List<ProjectSummary>();
        foreach (var project in projects)
        {
            var runCount = await db.Runs.CountAsync(r => r.ProjectId == project.Id, ct);
            var last = await db.Runs
                .AsNoTracking()
                .Include(r => r.Measurements)
                .Where(r => r.ProjectId == project.Id)
                .OrderByDescending(r => r.ExecutedAt)
                .FirstOrDefaultAsync(ct);
            var previous = last is null ? null : await PreviousOfAsync(last, ct);

            summaries.Add(new ProjectSummary(
                project.Key,
                project.Name,
                runCount,
                last is null ? null : ToInfo(last),
                last is null ? null : RunProgress.Compute(project.Goals, last.Measurements),
                previous is null ? null : RunProgress.Compute(project.Goals, previous.Measurements),
                last is null ? [] : Compare(project, last, previous, HighlightMetrics)));
        }

        return summaries;
    }

    /// <summary>
    /// Dashboard of a project on one branch: the selected run compared with the one before it.
    /// Defaults to the branch of the latest run and to the latest run of that branch.
    /// </summary>
    public async Task<ProjectDashboard?> GetProjectAsync(Guid organizationId, string projectKey, string? branch, Guid? runId, CancellationToken ct = default)
    {
        var project = await db.Projects
            .AsNoTracking()
            .Include(p => p.Components)
            .Include(p => p.Goals)
            .FirstOrDefaultAsync(p => p.OrganizationId == organizationId && p.Key == projectKey, ct);
        if (project is null)
            return null;

        var branches = (await db.Runs
                .Where(r => r.ProjectId == project.Id)
                .GroupBy(r => r.Branch)
                .Select(g => new { Name = g.Key, Count = g.Count(), Last = g.Max(r => r.ExecutedAt) })
                .ToListAsync(ct))
            .OrderByDescending(b => b.Last)
            .Select(b => new BranchInfo(b.Name, b.Count, b.Last))
            .ToList();
        if (branches.Count == 0)
            return new ProjectDashboard(project.Key, project.Name, [], null, null, null, null, null, [], [], []);

        var selectedBranch = branches.FirstOrDefault(b => b.Name == branch)?.Name ?? branches[0].Name;
        var runs = await db.Runs
            .AsNoTracking()
            .Include(r => r.Measurements)
            .Where(r => r.ProjectId == project.Id && r.Branch == selectedBranch)
            .OrderByDescending(r => r.ExecutedAt)
            .Take(HistorySize)
            .ToListAsync(ct);

        var selected = runs.FirstOrDefault(r => r.Id == runId) ?? runs[0];
        var index = runs.IndexOf(selected);
        var previous = index + 1 < runs.Count ? runs[index + 1] : await PreviousOfAsync(selected, ct);

        return new ProjectDashboard(
            project.Key,
            project.Name,
            branches,
            selectedBranch,
            ToInfo(selected),
            previous is null ? null : ToInfo(previous),
            RunProgress.Compute(project.Goals, selected.Measurements),
            previous is null ? null : RunProgress.Compute(project.Goals, previous.Measurements),
            Compare(project, selected, previous, onlyMetrics: null),
            runs.Select(r => new RunHistoryItem(ToInfo(r), RunProgress.Compute(project.Goals, r.Measurements))).ToList(),
            BuildTrends(project, runs));
    }

    private async Task<Run?> PreviousOfAsync(Run run, CancellationToken ct) =>
        await db.Runs
            .AsNoTracking()
            .Include(r => r.Measurements)
            .Where(r => r.ProjectId == run.ProjectId && r.Branch == run.Branch && r.Id != run.Id && r.ExecutedAt <= run.ExecutedAt)
            .OrderByDescending(r => r.ExecutedAt)
            .FirstOrDefaultAsync(ct);

    private static List<ComponentComparison> Compare(Project project, Run current, Run? previous, string[]? onlyMetrics)
    {
        var before = previous?.Measurements.ToDictionary(m => (m.ComponentId, m.MetricKey), m => m.Value) ?? [];
        var goals = project.Goals.ToDictionary(g => (g.ComponentId, g.MetricKey));

        return project.Components
            .OrderBy(c => c.Name)
            .Select(component =>
            {
                var metrics = current.Measurements
                    .Where(m => m.ComponentId == component.Id && (onlyMetrics is null || onlyMetrics.Contains(m.MetricKey)))
                    .OrderBy(m => MetricCatalog.Order(m.MetricKey))
                    .ThenBy(m => m.MetricKey)
                    .Select(m =>
                    {
                        var definition = MetricCatalog.Get(m.MetricKey);
                        double? previousValue = before.TryGetValue((component.Id, m.MetricKey), out var value) ? value : null;
                        var goal = goals.TryGetValue((component.Id, m.MetricKey), out var g)
                            ? new GoalStatus(
                                g.Baseline,
                                g.Target,
                                ProgressCalculator.GoalProgress(g.Baseline, g.Target, m.Value, definition.Direction),
                                ProgressCalculator.IsMet(g.Target, m.Value, definition.Direction))
                            : null;
                        var trend = previousValue is { } p ? ProgressCalculator.Judge(p, m.Value, definition.Direction) : Trend.Unchanged;
                        return new MetricComparison(definition, m.Value, previousValue, trend, goal);
                    })
                    .ToList();
                return new ComponentComparison(component.Id, component.Key, component.Name, metrics);
            })
            .Where(c => c.Metrics.Count > 0)
            .ToList();
    }

    private static List<TrendSeries> BuildTrends(Project project, List<Run> runsNewestFirst)
    {
        var chronological = runsNewestFirst.AsEnumerable().Reverse().ToList();
        var series = new List<TrendSeries>();
        foreach (var metricKey in TrendMetrics)
        {
            foreach (var component in project.Components.OrderBy(c => c.Name))
            {
                var points = chronological
                    .SelectMany(run => run.Measurements
                        .Where(m => m.ComponentId == component.Id && m.MetricKey == metricKey)
                        .Select(m => new TrendPoint(run.ExecutedAt, ShortCommit(run), m.Value)))
                    .ToList();
                if (points.Count > 0)
                    series.Add(new TrendSeries(component.Name, MetricCatalog.Get(metricKey), points));
            }
        }

        return series;
    }

    private static string ShortCommit(Run run) =>
        run.CommitSha is { Length: > 0 } sha ? sha[..Math.Min(7, sha.Length)] : "—";

    private static RunInfo ToInfo(Run run) => new(run.Id, run.Branch, run.CommitSha, run.IsDirty, run.Source, run.ExecutedAt);
}
