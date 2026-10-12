using Microsoft.EntityFrameworkCore;
using QualityTrack.Application.Abstractions;
using QualityTrack.Application.Common;
using QualityTrack.Domain.Entities;
using QualityTrack.Domain.Metrics;

namespace QualityTrack.Application.Goals;

public sealed record GoalItem(MetricDefinition Metric, double Current, double? Baseline, double? Target);

public sealed record ComponentGoals(Guid ComponentId, string Name, IReadOnlyList<GoalItem> Items);

public sealed record ProjectGoals(string Key, string Name, IReadOnlyList<ComponentGoals> Components);

public class GoalService(IAppDbContext db, TimeProvider clock)
{
    /// <summary>Metrics of the project's latest run that can carry a goal, with the goal they have (if any).</summary>
    public async Task<ProjectGoals?> GetAsync(Guid organizationId, string projectKey, CancellationToken ct = default)
    {
        var project = await db.Projects
            .AsNoTracking()
            .Include(p => p.Components)
            .Include(p => p.Goals)
            .FirstOrDefaultAsync(p => p.OrganizationId == organizationId && p.Key == projectKey, ct);
        if (project is null)
            return null;

        var latest = await LatestMeasurementsAsync(project.Id, ct);
        var components = project.Components
            .OrderBy(c => c.Name)
            .Select(component => new ComponentGoals(
                component.Id,
                component.Name,
                latest
                    .Where(m => m.ComponentId == component.Id && MetricCatalog.Get(m.MetricKey).Direction != MetricDirection.Neutral)
                    .OrderBy(m => MetricCatalog.Order(m.MetricKey))
                    .Select(m =>
                    {
                        var goal = project.Goals.FirstOrDefault(g => g.ComponentId == component.Id && g.MetricKey == m.MetricKey);
                        return new GoalItem(MetricCatalog.Get(m.MetricKey), m.Value, goal?.Baseline, goal?.Target);
                    })
                    .ToList()))
            .Where(c => c.Items.Count > 0)
            .ToList();

        return new ProjectGoals(project.Key, project.Name, components);
    }

    /// <summary>Sets the target of a metric. A new goal takes the metric's latest value as its baseline; an existing one keeps its own.</summary>
    public async Task SetAsync(Guid organizationId, string projectKey, Guid componentId, string metricKey, double target, CancellationToken ct = default)
    {
        if (!double.IsFinite(target))
            throw new ValidationException("La meta debe ser un número.");

        var project = await db.Projects
            .Include(p => p.Components)
            .Include(p => p.Goals)
            .FirstOrDefaultAsync(p => p.OrganizationId == organizationId && p.Key == projectKey, ct)
            ?? throw new ValidationException("Proyecto no encontrado.");
        if (project.Components.All(c => c.Id != componentId))
            throw new ValidationException("Componente no encontrado.");

        var goal = project.Goals.FirstOrDefault(g => g.ComponentId == componentId && g.MetricKey == metricKey);
        if (goal is null)
        {
            var latest = await LatestMeasurementsAsync(project.Id, ct);
            var current = latest.FirstOrDefault(m => m.ComponentId == componentId && m.MetricKey == metricKey)
                ?? throw new ValidationException("El componente aún no reporta esa métrica.");
            project.Goals.Add(new Goal
            {
                ComponentId = componentId,
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

    public async Task RemoveAsync(Guid organizationId, string projectKey, Guid componentId, string metricKey, CancellationToken ct = default)
    {
        var goal = await db.Goals.FirstOrDefaultAsync(
            g => g.Project!.OrganizationId == organizationId && g.Project.Key == projectKey && g.ComponentId == componentId && g.MetricKey == metricKey,
            ct);
        if (goal is null)
            return;

        db.Goals.Remove(goal);
        await db.SaveChangesAsync(ct);
    }

    private async Task<List<Measurement>> LatestMeasurementsAsync(Guid projectId, CancellationToken ct)
    {
        var latestRunId = await db.Runs
            .Where(r => r.ProjectId == projectId)
            .OrderByDescending(r => r.ExecutedAt)
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(ct);
        return latestRunId is null
            ? []
            : await db.Measurements.AsNoTracking().Where(m => m.RunId == latestRunId).ToListAsync(ct);
    }
}
