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
        [MetricCatalog.Bugs, MetricCatalog.Coverage, MetricCatalog.Vulnerabilities, MetricCatalog.CodeSmells, MetricCatalog.TechnicalDebtMinutes];

    public async Task<IReadOnlyList<SolutionSummary>> GetOverviewAsync(Guid organizationId, CancellationToken ct = default)
    {
        var solutions = await db.Solutions
            .AsNoTracking()
            .Include(p => p.Projects)
            .Include(p => p.Goals)
            .Where(p => p.OrganizationId == organizationId)
            .OrderBy(p => p.Name)
            .ToListAsync(ct);

        var summaries = new List<SolutionSummary>();
        foreach (var solution in solutions)
        {
            var runCount = await db.Runs.CountAsync(r => r.SolutionId == solution.Id, ct);
            var last = await db.Runs
                .AsNoTracking()
                .Include(r => r.Measurements)
                .Where(r => r.SolutionId == solution.Id)
                .OrderByDescending(r => r.ExecutedAt)
                .FirstOrDefaultAsync(ct);
            var previous = last is null ? null : await PreviousOfAsync(last, ct);

            summaries.Add(new SolutionSummary(
                solution.Key,
                solution.Name,
                runCount,
                last is null ? null : ToInfo(last),
                last is null ? null : RunProgress.Compute(solution.Goals, last.Measurements),
                previous is null ? null : RunProgress.Compute(solution.Goals, previous.Measurements),
                last is null ? [] : RunComparer.Compare(solution, last, previous, HighlightMetrics)));
        }

        return summaries;
    }

    /// <summary>
    /// Dashboard of a solution on one branch: the selected run compared with the one before it.
    /// Defaults to the branch of the latest run and to the latest run of that branch.
    /// </summary>
    public async Task<SolutionDashboard?> GetSolutionAsync(Guid organizationId, string solutionKey, string? branch, Guid? runId, CancellationToken ct = default)
    {
        var solution = await db.Solutions
            .AsNoTracking()
            .Include(p => p.Projects)
            .Include(p => p.Goals)
            .FirstOrDefaultAsync(p => p.OrganizationId == organizationId && p.Key == solutionKey, ct);
        if (solution is null)
            return null;

        var branches = (await db.Runs
                .Where(r => r.SolutionId == solution.Id)
                .GroupBy(r => r.Branch)
                .Select(g => new { Name = g.Key, Count = g.Count(), Last = g.Max(r => r.ExecutedAt) })
                .ToListAsync(ct))
            .OrderByDescending(b => b.Last)
            .Select(b => new BranchInfo(b.Name, b.Count, b.Last))
            .ToList();
        if (branches.Count == 0)
            return new SolutionDashboard(solution.Key, solution.Name, [], null, null, null, null, null, [], [], []);

        var selectedBranch = branches.FirstOrDefault(b => b.Name == branch)?.Name ?? branches[0].Name;
        var runs = await db.Runs
            .AsNoTracking()
            .Include(r => r.Measurements)
            .Where(r => r.SolutionId == solution.Id && r.Branch == selectedBranch)
            .OrderByDescending(r => r.ExecutedAt)
            .Take(HistorySize)
            .ToListAsync(ct);

        var selected = runs.FirstOrDefault(r => r.Id == runId) ?? runs[0];
        var index = runs.IndexOf(selected);
        var previous = index + 1 < runs.Count ? runs[index + 1] : await PreviousOfAsync(selected, ct);

        var withFindings = (await db.Findings
                .Where(f => f.RunId == selected.Id)
                .Select(f => new { f.ProjectId, f.MetricKey })
                .Distinct()
                .ToListAsync(ct))
            .Select(f => (f.ProjectId, f.MetricKey))
            .ToHashSet();

        return new SolutionDashboard(
            solution.Key,
            solution.Name,
            branches,
            selectedBranch,
            ToInfo(selected),
            previous is null ? null : ToInfo(previous),
            RunProgress.Compute(solution.Goals, selected.Measurements),
            previous is null ? null : RunProgress.Compute(solution.Goals, previous.Measurements),
            RunComparer.Compare(solution, selected, previous, onlyMetrics: null, withFindings),
            runs.Select(r => new RunHistoryItem(ToInfo(r), RunProgress.Compute(solution.Goals, r.Measurements))).ToList(),
            BuildTrends(solution, runs));
    }

    private async Task<Run?> PreviousOfAsync(Run run, CancellationToken ct) =>
        await db.Runs
            .AsNoTracking()
            .Include(r => r.Measurements)
            .Where(r => r.SolutionId == run.SolutionId && r.Branch == run.Branch && r.Id != run.Id && r.ExecutedAt <= run.ExecutedAt)
            .OrderByDescending(r => r.ExecutedAt)
            .FirstOrDefaultAsync(ct);

    private static List<TrendSeries> BuildTrends(Solution solution, List<Run> runsNewestFirst)
    {
        var chronological = runsNewestFirst.AsEnumerable().Reverse().ToList();
        var series = new List<TrendSeries>();
        foreach (var metricKey in TrendMetrics)
        {
            foreach (var project in solution.Projects.OrderBy(c => c.Name))
            {
                var points = chronological
                    .SelectMany(run => run.Measurements
                        .Where(m => m.ProjectId == project.Id && m.MetricKey == metricKey)
                        .Select(m => new TrendPoint(run.ExecutedAt, ShortCommit(run), m.Value)))
                    .ToList();
                if (points.Count > 0)
                    series.Add(new TrendSeries(project.Key, project.Name, MetricCatalog.Get(metricKey), points));
            }
        }

        return series;
    }

    private static string ShortCommit(Run run) =>
        run.CommitSha is { Length: > 0 } sha ? sha[..Math.Min(7, sha.Length)] : "—";

    private static RunInfo ToInfo(Run run) => new(run.Id, run.Branch, run.CommitSha, run.IsDirty, run.Source, run.ExecutedAt);
}
