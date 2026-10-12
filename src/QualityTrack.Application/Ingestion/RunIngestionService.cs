using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using QualityTrack.Application.Abstractions;
using QualityTrack.Application.Common;
using QualityTrack.Application.Dashboard;
using QualityTrack.Domain.Entities;
using QualityTrack.Domain.Metrics;

namespace QualityTrack.Application.Ingestion;

public partial class RunIngestionService(IAppDbContext db, TimeProvider clock)
{
    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,63}$")]
    private static partial Regex KeyPattern();

    [GeneratedRegex("^[a-z0-9][a-z0-9_.-]{0,79}$")]
    private static partial Regex MetricPattern();

    private const int MaxFindingsPerProject = 20_000;

    public async Task<IngestRunResult> IngestAsync(Guid organizationId, IngestRunRequest request, CancellationToken ct = default)
    {
        var solutionKey = request.Solution?.Trim().ToLowerInvariant() ?? "";
        var branch = request.Branch?.Trim() ?? "";
        Validate(request, solutionKey, branch);

        var now = clock.GetUtcNow().UtcDateTime;
        var solution = await db.Solutions
            .Include(p => p.Projects)
            .Include(p => p.Goals)
            .FirstOrDefaultAsync(p => p.OrganizationId == organizationId && p.Key == solutionKey, ct);
        var solutionCreated = solution is null;
        if (solution is null)
        {
            solution = new Solution
            {
                OrganizationId = organizationId,
                Key = solutionKey,
                Name = string.IsNullOrWhiteSpace(request.SolutionName) ? solutionKey : request.SolutionName.Trim(),
                CreatedAt = now
            };
            db.Solutions.Add(solution);
        }

        var run = new Run
        {
            Solution = solution,
            Branch = branch,
            CommitSha = string.IsNullOrWhiteSpace(request.Commit) ? null : request.Commit.Trim(),
            IsDirty = request.Dirty,
            Source = string.IsNullOrWhiteSpace(request.Source) ? null : request.Source.Trim(),
            ExecutedAt = request.ExecutedAt?.UtcDateTime ?? now
        };
        db.Runs.Add(run);

        foreach (var item in request.Projects!)
        {
            var projectKey = item.Key!.Trim().ToLowerInvariant();
            var project = solution.Projects.FirstOrDefault(c => c.Key == projectKey);
            var projectCreated = project is null;
            if (project is null)
            {
                project = new Project
                {
                    Key = projectKey,
                    Name = string.IsNullOrWhiteSpace(item.Name) ? projectKey : item.Name.Trim()
                };
                solution.Projects.Add(project);
            }

            foreach (var finding in item.Findings ?? [])
                db.Findings.Add(ToFinding(finding, run, project));

            var values = CollectValues(item);
            var alreadyReported = projectCreated
                ? []
                : await db.Measurements
                    .Where(m => m.ProjectId == project.Id)
                    .Select(m => m.MetricKey)
                    .Distinct()
                    .ToListAsync(ct);

            foreach (var (metricKey, value) in values)
            {
                run.Measurements.Add(new Measurement { Project = project, MetricKey = metricKey, Value = value });

                // A default goal is set only the first time a project reports the metric, so a goal
                // the user removed later is not brought back by the next run.
                if (MetricCatalog.DefaultTargets.TryGetValue(metricKey, out var target)
                    && !alreadyReported.Contains(metricKey)
                    && !solution.Goals.Any(g => g.ProjectId == project.Id && g.MetricKey == metricKey))
                {
                    solution.Goals.Add(new Goal
                    {
                        Project = project,
                        ProjectId = project.Id,
                        MetricKey = metricKey,
                        Baseline = value,
                        Target = target,
                        CreatedAt = now
                    });
                }
            }
        }

        // Read before saving so the run being stored is not its own predecessor.
        var previous = solutionCreated
            ? null
            : await db.Runs
                .Include(r => r.Measurements)
                .Where(r => r.SolutionId == solution.Id && r.Branch == branch && r.ExecutedAt <= run.ExecutedAt)
                .OrderByDescending(r => r.ExecutedAt)
                .FirstOrDefaultAsync(ct);

        var reportsFindings = request.Projects!.Any(p => p.Findings is not null);
        if (reportsFindings)
            await UpsertRulesAsync(solution, solutionCreated, request.Rules ?? [], ct);

        await db.SaveChangesAsync(ct);

        // Findings carry source code, so only the latest set of each branch is kept. A run that reports
        // none (findings omitted) leaves the stored ones alone.
        if (reportsFindings && !solutionCreated)
        {
            await db.Findings
                .Where(f => f.RunId != run.Id && f.Run!.SolutionId == solution.Id && f.Run.Branch == branch)
                .ExecuteDeleteAsync(ct);
        }

        return new IngestRunResult(
            run.Id,
            solution.Key,
            run.Branch,
            solutionCreated,
            RunProgress.Compute(solution.Goals, run.Measurements),
            previous is null ? null : RunProgress.Compute(solution.Goals, previous.Measurements),
            RunComparer.Compare(solution, run, previous, onlyMetrics: null)
                .Select(c => new IngestedProject(
                    c.Key,
                    c.Name,
                    c.Metrics
                        .Select(m => new IngestedMetric(m.Metric.Key, m.Metric.Name, m.Metric.Category, m.Metric.Unit, m.Current, m.Previous, m.Delta, m.Trend, m.Goal))
                        .ToList()))
                .ToList());
    }

    private const int MaxSnippetLines = 60;
    private const int MaxSnippetLineLength = 400;

    private async Task UpsertRulesAsync(Solution solution, bool solutionCreated, List<IngestRule> rules, CancellationToken ct)
    {
        var existing = solutionCreated
            ? []
            : await db.Rules.Where(r => r.SolutionId == solution.Id).ToDictionaryAsync(r => r.Key, ct);

        foreach (var item in rules.DistinctBy(r => r.Key!.Trim()))
        {
            var key = item.Key!.Trim();
            if (!existing.TryGetValue(key, out var rule))
            {
                rule = new Rule { Solution = solution, Key = key, Name = key };
                db.Rules.Add(rule);
            }

            rule.Name = Truncate(string.IsNullOrWhiteSpace(item.Name) ? key : item.Name.Trim(), 500)!;
            rule.Description = Truncate(NullIfBlank(item.Description), 20_000);
            rule.HowToFix = Truncate(NullIfBlank(item.HowToFix), 20_000);
        }
    }

    private static Finding ToFinding(IngestFinding item, Run run, Project project)
    {
        var lines = item.Snippet?.Lines is { Count: > 0 } snippet
            ? snippet.Take(MaxSnippetLines).Select(line => Truncate(line, MaxSnippetLineLength))
            : null;

        return new Finding
        {
            Run = run,
            Project = project,
            MetricKey = item.Metric!.Trim().ToLowerInvariant(),
            RuleKey = item.Rule!.Trim(),
            Severity = NullIfBlank(item.Severity)?.ToLowerInvariant(),
            Message = Truncate(item.Message!.Trim(), 2000)!,
            FilePath = item.File!.Trim().Replace('\\', '/'),
            Line = item.Line,
            EndLine = item.EndLine ?? item.Line,
            StartColumn = item.StartColumn,
            EndColumn = item.EndColumn,
            Snippet = lines is null ? null : string.Join('\n', lines),
            SnippetStartLine = lines is null ? null : item.Snippet!.StartLine
        };
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Truncate(string? value, int max) => value is { Length: var length } && length > max ? value[..max] : value;

    private static Dictionary<string, double> CollectValues(IngestProject item)
    {
        var values = new Dictionary<string, double>();
        if (item.Tests is { } tests)
        {
            values[MetricCatalog.TestsTotal] = tests.Total;
            values[MetricCatalog.TestsPassed] = tests.Passed;
            values[MetricCatalog.TestsFailed] = tests.Failed;
            values[MetricCatalog.TestsSkipped] = tests.Skipped;
            if (tests.DurationSeconds is { } duration)
                values[MetricCatalog.TestsDurationSeconds] = duration;
        }

        foreach (var (key, value) in item.Metrics ?? [])
            values[key.Trim().ToLowerInvariant()] = value;

        return values;
    }

    private static void Validate(IngestRunRequest request, string solutionKey, string branch)
    {
        var errors = new List<string>();
        if (!KeyPattern().IsMatch(solutionKey))
            errors.Add("'solution' is required: lowercase letters, digits, '.', '_' or '-', up to 64 characters.");
        if (branch.Length is 0 or > 200)
            errors.Add("'branch' is required (up to 200 characters).");
        if (request.Commit?.Trim().Length > 64)
            errors.Add("'commit' accepts up to 64 characters.");
        if (request.Projects is not { Count: > 0 })
            errors.Add("'projects' needs at least one project.");

        var seen = new HashSet<string>();
        foreach (var project in request.Projects ?? [])
        {
            var key = project.Key?.Trim().ToLowerInvariant() ?? "";
            if (!KeyPattern().IsMatch(key))
            {
                errors.Add("Every project needs a 'key': lowercase letters, digits, '.', '_' or '-', up to 64 characters.");
                continue;
            }

            if (!seen.Add(key))
                errors.Add($"Project '{key}' is repeated.");
            if (project.Tests is null && project.Metrics is not { Count: > 0 })
                errors.Add($"Project '{key}' has neither 'tests' nor 'metrics'.");
            if (project.Tests is { } t && (t.Total < 0 || t.Passed < 0 || t.Failed < 0 || t.Skipped < 0))
                errors.Add($"Project '{key}': test counts cannot be negative.");

            foreach (var (metricKey, value) in project.Metrics ?? [])
            {
                if (!MetricPattern().IsMatch(metricKey.Trim().ToLowerInvariant()))
                    errors.Add($"Project '{key}': metric key '{metricKey}' is not valid.");
                if (!double.IsFinite(value))
                    errors.Add($"Project '{key}': metric '{metricKey}' must be a finite number.");
            }

            if (project.Findings is { Count: > MaxFindingsPerProject })
                errors.Add($"Project '{key}': at most {MaxFindingsPerProject} findings per run.");

            // One message per kind of problem: a bad export would otherwise repeat it thousands of times.
            var findings = project.Findings ?? [];
            if (findings.Any(f => !MetricPattern().IsMatch(f.Metric?.Trim().ToLowerInvariant() ?? "")))
                errors.Add($"Project '{key}': every finding needs a valid 'metric' key.");
            if (findings.Any(f => string.IsNullOrWhiteSpace(f.Rule) || f.Rule.Trim().Length > 200))
                errors.Add($"Project '{key}': every finding needs a 'rule' (up to 200 characters).");
            if (findings.Any(f => string.IsNullOrWhiteSpace(f.Message)))
                errors.Add($"Project '{key}': every finding needs a 'message'.");
            if (findings.Any(f => string.IsNullOrWhiteSpace(f.File) || f.File.Trim().Length > 1000))
                errors.Add($"Project '{key}': every finding needs a 'file' (up to 1000 characters).");
            if (findings.Any(f => f.Line < 1 || f.EndLine < f.Line || f.StartColumn < 0 || f.EndColumn < 0 || f.Snippet?.StartLine < 1))
                errors.Add($"Project '{key}': finding positions must be positive, with 'endLine' not before 'line'.");
        }

        if ((request.Rules ?? []).Any(r => string.IsNullOrWhiteSpace(r.Key) || r.Key.Trim().Length > 200))
            errors.Add("Every rule needs a 'key' (up to 200 characters).");

        if (errors.Count > 0)
            throw new ValidationException(errors);
    }
}
