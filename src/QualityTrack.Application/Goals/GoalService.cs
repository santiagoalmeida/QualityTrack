using Microsoft.EntityFrameworkCore;
using QualityTrack.Application.Abstractions;
using QualityTrack.Application.Common;
using QualityTrack.Domain.Entities;
using QualityTrack.Domain.Metrics;

namespace QualityTrack.Application.Goals;

public sealed record GoalItem(MetricDefinition Metric, double Current, double? Baseline, double? Target);

public sealed record ProjectGoals(Guid ProjectId, string Name, IReadOnlyList<GoalItem> Items);

public sealed record SolutionGoals(string Key, string Name, IReadOnlyList<ProjectGoals> Projects);

public class GoalService(IAppDbContext db, TimeProvider clock)
{
    /// <summary>Metrics of the solution's latest run that can carry a goal, with the goal they have (if any).</summary>
    public async Task<SolutionGoals?> GetAsync(Guid organizationId, string solutionKey, CancellationToken ct = default)
    {
        var solution = await db.Solutions
            .AsNoTracking()
            .Include(p => p.Projects)
            .Include(p => p.Goals)
            .FirstOrDefaultAsync(p => p.OrganizationId == organizationId && p.Key == solutionKey, ct);
        if (solution is null)
            return null;

        var latest = await LatestMeasurementsAsync(solution.Id, ct);
        var projects = solution.Projects
            .OrderBy(c => c.Name)
            .Select(project => new ProjectGoals(
                project.Id,
                project.Name,
                latest
                    .Where(m => m.ProjectId == project.Id && MetricCatalog.Get(m.MetricKey).Direction != MetricDirection.Neutral)
                    .OrderBy(m => MetricCatalog.Order(m.MetricKey))
                    .Select(m =>
                    {
                        var goal = solution.Goals.FirstOrDefault(g => g.ProjectId == project.Id && g.MetricKey == m.MetricKey);
                        return new GoalItem(MetricCatalog.Get(m.MetricKey), m.Value, goal?.Baseline, goal?.Target);
                    })
                    .ToList()))
            .Where(c => c.Items.Count > 0)
            .ToList();

        return new SolutionGoals(solution.Key, solution.Name, projects);
    }

    /// <summary>Sets the target of a metric. A new goal takes the metric's latest value as its baseline; an existing one keeps its own.</summary>
    public async Task SetAsync(Guid organizationId, string solutionKey, Guid projectId, string metricKey, double target, CancellationToken ct = default)
    {
        if (!double.IsFinite(target))
            throw new ValidationException("La meta debe ser un número.");

        var solution = await db.Solutions
            .Include(p => p.Projects)
            .Include(p => p.Goals)
            .FirstOrDefaultAsync(p => p.OrganizationId == organizationId && p.Key == solutionKey, ct)
            ?? throw new ValidationException("Solución no encontrada.");
        if (solution.Projects.All(c => c.Id != projectId))
            throw new ValidationException("Proyecto no encontrado.");

        var goal = solution.Goals.FirstOrDefault(g => g.ProjectId == projectId && g.MetricKey == metricKey);
        if (goal is null)
        {
            var latest = await LatestMeasurementsAsync(solution.Id, ct);
            var current = latest.FirstOrDefault(m => m.ProjectId == projectId && m.MetricKey == metricKey)
                ?? throw new ValidationException("El proyecto aún no reporta esa métrica.");
            solution.Goals.Add(new Goal
            {
                ProjectId = projectId,
                MetricKey = metricKey,
                Baseline = current.Value,
                Target = target,
                CreatedAt = clock.GetUtcNow().UtcDateTime
            });
        }
        else
        {
            goal.Target = target;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(Guid organizationId, string solutionKey, Guid projectId, string metricKey, CancellationToken ct = default)
    {
        var goal = await db.Goals.FirstOrDefaultAsync(
            g => g.Solution!.OrganizationId == organizationId && g.Solution.Key == solutionKey && g.ProjectId == projectId && g.MetricKey == metricKey,
            ct);
        if (goal is null)
            return;

        db.Goals.Remove(goal);
        await db.SaveChangesAsync(ct);
    }

    private async Task<List<Measurement>> LatestMeasurementsAsync(Guid solutionId, CancellationToken ct)
    {
        var latestRunId = await db.Runs
            .Where(r => r.SolutionId == solutionId)
            .OrderByDescending(r => r.ExecutedAt)
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(ct);
        return latestRunId is null
            ? []
            : await db.Measurements.AsNoTracking().Where(m => m.RunId == latestRunId).ToListAsync(ct);
    }
}
