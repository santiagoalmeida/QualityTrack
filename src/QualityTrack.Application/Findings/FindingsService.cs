using Microsoft.EntityFrameworkCore;
using QualityTrack.Application.Abstractions;
using QualityTrack.Application.Dashboard;
using QualityTrack.Domain.Entities;
using QualityTrack.Domain.Metrics;

namespace QualityTrack.Application.Findings;

/// <summary>Where a list of findings belongs: the metric of a project in a run.</summary>
public sealed record FindingsContext(string SolutionKey, string SolutionName, string ProjectKey, string ProjectName, MetricDefinition Metric, RunInfo Run);

/// <param name="Files">Number of distinct files with findings of the rule.</param>
/// <param name="TopSeverity">Worst severity among its findings, if the analyzer reports one.</param>
public sealed record RuleSummary(string Key, string Name, int Count, int Files, string? TopSeverity);

public sealed record RuleList(FindingsContext Context, int Total, IReadOnlyList<RuleSummary> Rules);

public sealed record FindingDetail(
    string Message,
    string? Severity,
    string FilePath,
    int? Line,
    int? EndLine,
    int? StartColumn,
    int? EndColumn,
    IReadOnlyList<string> SnippetLines,
    int? SnippetStartLine);

/// <param name="Total">Findings of the rule; <paramref name="Findings"/> may hold fewer.</param>
public sealed record RuleFindings(
    FindingsContext Context,
    string RuleKey,
    string RuleName,
    string? Description,
    string? HowToFix,
    int Total,
    IReadOnlyList<FindingDetail> Findings);

public class FindingsService(IAppDbContext db)
{
    public const int PageSize = 100;

    private static readonly string[] SeverityOrder = ["blocker", "critical", "high", "major", "medium", "minor", "low", "info"];

    /// <summary>Rules with findings for a metric of a project, most frequent first.</summary>
    public async Task<RuleList?> GetRulesAsync(Guid organizationId, string solutionKey, string projectKey, string metricKey, string? branch, Guid? runId, CancellationToken ct = default)
    {
        var scope = await ResolveAsync(organizationId, solutionKey, projectKey, metricKey, branch, runId, ct);
        if (scope is null)
            return null;

        var rows = await db.Findings
            .AsNoTracking()
            .Where(f => f.RunId == scope.Run.Id && f.ProjectId == scope.Project.Id && f.MetricKey == metricKey)
            .Select(f => new { f.RuleKey, f.Severity, f.FilePath })
            .ToListAsync(ct);
        var names = await db.Rules
            .AsNoTracking()
            .Where(r => r.SolutionId == scope.Solution.Id)
            .ToDictionaryAsync(r => r.Key, r => r.Name, ct);

        var rules = rows
            .GroupBy(r => r.RuleKey)
            .Select(g => new RuleSummary(
                g.Key,
                names.GetValueOrDefault(g.Key, g.Key),
                g.Count(),
                g.Select(r => r.FilePath).Distinct().Count(),
                g.Select(r => r.Severity).Where(s => s is not null).OrderBy(SeverityRank).FirstOrDefault()))
            .OrderByDescending(r => r.Count)
            .ThenBy(r => r.Name)
            .ToList();

        return new RuleList(scope.Context, rows.Count, rules);
    }

    /// <summary>Findings of one rule with their code, ordered by file and line. At most <see cref="PageSize"/>.</summary>
    public async Task<RuleFindings?> GetRuleFindingsAsync(Guid organizationId, string solutionKey, string projectKey, string metricKey, string ruleKey, string? branch, Guid? runId, CancellationToken ct = default)
    {
        var scope = await ResolveAsync(organizationId, solutionKey, projectKey, metricKey, branch, runId, ct);
        if (scope is null)
            return null;

        var query = db.Findings
            .AsNoTracking()
            .Where(f => f.RunId == scope.Run.Id && f.ProjectId == scope.Project.Id && f.MetricKey == metricKey && f.RuleKey == ruleKey);
        var total = await query.CountAsync(ct);
        if (total == 0)
            return null;

        var findings = await query.OrderBy(f => f.FilePath).ThenBy(f => f.Line).Take(PageSize).ToListAsync(ct);
        var rule = await db.Rules.AsNoTracking().FirstOrDefaultAsync(r => r.SolutionId == scope.Solution.Id && r.Key == ruleKey, ct);

        return new RuleFindings(
            scope.Context,
            ruleKey,
            rule?.Name ?? ruleKey,
            rule?.Description,
            rule?.HowToFix,
            total,
            findings
                .Select(f => new FindingDetail(
                    f.Message,
                    f.Severity,
                    f.FilePath,
                    f.Line,
                    f.EndLine,
                    f.StartColumn,
                    f.EndColumn,
                    f.Snippet is null ? [] : f.Snippet.Split('\n'),
                    f.SnippetStartLine))
                .ToList());
    }

    private static int SeverityRank(string? severity)
    {
        var index = Array.IndexOf(SeverityOrder, severity);
        return index < 0 ? SeverityOrder.Length : index;
    }

    private sealed record Scope(Solution Solution, Project Project, Run Run, FindingsContext Context);

    // The run given, or the latest of the branch, or the latest of the solution - the same default as the dashboard.
    private async Task<Scope?> ResolveAsync(Guid organizationId, string solutionKey, string projectKey, string metricKey, string? branch, Guid? runId, CancellationToken ct)
    {
        var solution = await db.Solutions
            .AsNoTracking()
            .Include(s => s.Projects)
            .FirstOrDefaultAsync(s => s.OrganizationId == organizationId && s.Key == solutionKey, ct);
        var project = solution?.Projects.FirstOrDefault(p => p.Key == projectKey);
        if (solution is null || project is null)
            return null;

        var runs = db.Runs.AsNoTracking().Where(r => r.SolutionId == solution.Id);
        var run = runId is { } id ? await runs.FirstOrDefaultAsync(r => r.Id == id, ct) : null;
        run ??= branch is null ? null : await runs.Where(r => r.Branch == branch).OrderByDescending(r => r.ExecutedAt).FirstOrDefaultAsync(ct);
        run ??= await runs.OrderByDescending(r => r.ExecutedAt).FirstOrDefaultAsync(ct);
        if (run is null)
            return null;

        var info = new RunInfo(run.Id, run.Branch, run.CommitSha, run.IsDirty, run.Source, run.ExecutedAt);
        return new Scope(solution, project, run, new FindingsContext(solution.Key, solution.Name, project.Key, project.Name, MetricCatalog.Get(metricKey), info));
    }
}
