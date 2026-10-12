using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using QualityTrack.Application.Abstractions;
using QualityTrack.Application.Common;
using QualityTrack.Domain.Entities;
using QualityTrack.Domain.Metrics;

namespace QualityTrack.Application.Ingestion;

public partial class RunIngestionService(IAppDbContext db, TimeProvider clock)
{
    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,63}$")]
    private static partial Regex KeyPattern();

    [GeneratedRegex("^[a-z0-9][a-z0-9_.-]{0,79}$")]
    private static partial Regex MetricPattern();

    public async Task<IngestRunResult> IngestAsync(Guid organizationId, IngestRunRequest request, CancellationToken ct = default)
    {
        var projectKey = request.Project?.Trim().ToLowerInvariant() ?? "";
        var branch = request.Branch?.Trim() ?? "";
        Validate(request, projectKey, branch);

        var now = clock.GetUtcNow().UtcDateTime;
        var project = await db.Projects
            .Include(p => p.Components)
            .Include(p => p.Goals)
            .FirstOrDefaultAsync(p => p.OrganizationId == organizationId && p.Key == projectKey, ct);
        var projectCreated = project is null;
        if (project is null)
        {
            project = new Project
            {
                OrganizationId = organizationId,
                Key = projectKey,
                Name = string.IsNullOrWhiteSpace(request.ProjectName) ? projectKey : request.ProjectName.Trim(),
                CreatedAt = now
            };
            db.Projects.Add(project);
        }

        var run = new Run
        {
            Project = project,
            Branch = branch,
            CommitSha = string.IsNullOrWhiteSpace(request.Commit) ? null : request.Commit.Trim(),
            IsDirty = request.Dirty,
            Source = string.IsNullOrWhiteSpace(request.Source) ? null : request.Source.Trim(),
            ExecutedAt = request.ExecutedAt?.UtcDateTime ?? now
        };
        db.Runs.Add(run);

        foreach (var item in request.Components!)
        {
            var componentKey = item.Key!.Trim().ToLowerInvariant();
            var component = project.Components.FirstOrDefault(c => c.Key == componentKey);
            var componentCreated = component is null;
            if (component is null)
            {
                component = new Component
                {
                    Key = componentKey,
                    Name = string.IsNullOrWhiteSpace(item.Name) ? componentKey : item.Name.Trim()
                };
                project.Components.Add(component);
            }

            var values = CollectValues(item);
            var alreadyReported = componentCreated
                ? []
                : await db.Measurements
                    .Where(m => m.ComponentId == component.Id)
                    .Select(m => m.MetricKey)
                    .Distinct()
                    .ToListAsync(ct);

            foreach (var (metricKey, value) in values)
            {
                run.Measurements.Add(new Measurement { Component = component, MetricKey = metricKey, Value = value });

                // A default goal is set only the first time a component reports the metric, so a goal
                // the user removed later is not brought back by the next run.
                if (MetricCatalog.DefaultTargets.TryGetValue(metricKey, out var target)
                    && !alreadyReported.Contains(metricKey)
                    && !project.Goals.Any(g => g.ComponentId == component.Id && g.MetricKey == metricKey))
                {
                    project.Goals.Add(new Goal
                    {
                        Component = component,
                        ComponentId = component.Id,
                        MetricKey = metricKey,
                        Baseline = value,
                        Target = target,
                        CreatedAt = now
                    });
                }
            }
        }

        // Read before saving so the run being stored is not its own predecessor.
        var previous = projectCreated
            ? null
            : await db.Runs
                .Include(r => r.Measurements)
                .Where(r => r.ProjectId == project.Id && r.Branch == branch && r.ExecutedAt <= run.ExecutedAt)
                .OrderByDescending(r => r.ExecutedAt)
                .FirstOrDefaultAsync(ct);

        await db.SaveChangesAsync(ct);

        return new IngestRunResult(
            run.Id,
            project.Key,
            run.Branch,
            projectCreated,
            RunProgress.Compute(project.Goals, run.Measurements),
            previous is null ? null : RunProgress.Compute(project.Goals, previous.Measurements));
    }

    private static Dictionary<string, double> CollectValues(IngestComponent item)
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

    private static void Validate(IngestRunRequest request, string projectKey, string branch)
    {
        var errors = new List<string>();
        if (!KeyPattern().IsMatch(projectKey))
            errors.Add("'project' is required: lowercase letters, digits, '.', '_' or '-', up to 64 characters.");
        if (branch.Length is 0 or > 200)
            errors.Add("'branch' is required (up to 200 characters).");
        if (request.Commit?.Trim().Length > 64)
            errors.Add("'commit' accepts up to 64 characters.");
        if (request.Components is not { Count: > 0 })
            errors.Add("'components' needs at least one component.");

        var seen = new HashSet<string>();
        foreach (var component in request.Components ?? [])
        {
            var key = component.Key?.Trim().ToLowerInvariant() ?? "";
            if (!KeyPattern().IsMatch(key))
            {
                errors.Add("Every component needs a 'key': lowercase letters, digits, '.', '_' or '-', up to 64 characters.");
                continue;
            }

            if (!seen.Add(key))
                errors.Add($"Component '{key}' is repeated.");
            if (component.Tests is null && component.Metrics is not { Count: > 0 })
                errors.Add($"Component '{key}' has neither 'tests' nor 'metrics'.");
            if (component.Tests is { } t && (t.Total < 0 || t.Passed < 0 || t.Failed < 0 || t.Skipped < 0))
                errors.Add($"Component '{key}': test counts cannot be negative.");

            foreach (var (metricKey, value) in component.Metrics ?? [])
            {
                if (!MetricPattern().IsMatch(metricKey.Trim().ToLowerInvariant()))
                    errors.Add($"Component '{key}': metric key '{metricKey}' is not valid.");
                if (!double.IsFinite(value))
                    errors.Add($"Component '{key}': metric '{metricKey}' must be a finite number.");
            }
        }

        if (errors.Count > 0)
            throw new ValidationException(errors);
    }
}
