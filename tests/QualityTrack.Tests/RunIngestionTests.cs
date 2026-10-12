using Microsoft.EntityFrameworkCore;
using QualityTrack.Application.Common;
using QualityTrack.Application.Dashboard;
using QualityTrack.Application.Goals;
using QualityTrack.Application.Ingestion;
using QualityTrack.Domain.Progress;

namespace QualityTrack.Tests;

public sealed class RunIngestionTests : IDisposable
{
    private readonly TestDatabase _database = new();
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly Guid _organizationId;

    public RunIngestionTests() => _organizationId = _database.AddOrganization();

    public void Dispose() => _database.Dispose();

    private static IngestRunRequest Request(double coverage, double bugs, string branch = "main", int failed = 0) => new()
    {
        Project = "Shop",
        ProjectName = "Tienda",
        Branch = branch,
        Commit = "abc1234def",
        Components =
        [
            new IngestComponent
            {
                Key = "backend",
                Name = "Backend",
                Tests = new IngestTests { Total = 10, Passed = 10 - failed, Failed = failed },
                Metrics = new() { ["coverage"] = coverage, ["bugs"] = bugs, ["bundle_size_kb"] = 512 }
            }
        ]
    };

    private async Task<IngestRunResult> IngestAsync(IngestRunRequest request, Guid? organizationId = null)
    {
        await using var db = _database.NewContext();
        var result = await new RunIngestionService(db, _clock).IngestAsync(organizationId ?? _organizationId, request);
        _clock.Advance(TimeSpan.FromHours(1));
        return result;
    }

    [Fact]
    public async Task FirstRun_CreatesProjectComponentMeasurementsAndDefaultGoals()
    {
        var result = await IngestAsync(Request(coverage: 60, bugs: 4));

        await using var db = _database.NewContext();
        var project = await db.Projects.Include(p => p.Components).Include(p => p.Goals).SingleAsync();
        Assert.True(result.ProjectCreated);
        Assert.Equal(("shop", "Tienda"), (project.Key, project.Name));
        Assert.Equal("Backend", Assert.Single(project.Components).Name);
        // 4 test counts + 3 metrics.
        Assert.Equal(7, await db.Measurements.CountAsync());
        Assert.Equal(
            [("bugs", 4d, 0d), ("coverage", 60d, 80d), ("tests_failed", 0d, 0d)],
            project.Goals.OrderBy(g => g.MetricKey).Select(g => (g.MetricKey, g.Baseline, g.Target)));
        // coverage 0 %, bugs 0 %, failed tests already at target.
        Assert.Equal(1 / 3d, result.Progress!.Value, 6);
        Assert.Null(result.PreviousProgress);
    }

    [Fact]
    public async Task SecondRun_ReportsProgressAgainstThePreviousOne()
    {
        await IngestAsync(Request(coverage: 60, bugs: 4));

        var result = await IngestAsync(Request(coverage: 70, bugs: 0));

        Assert.False(result.ProjectCreated);
        Assert.Equal(1 / 3d, result.PreviousProgress!.Value, 6);
        // coverage halfway, bugs done, failed tests done.
        Assert.Equal((0.5 + 1 + 1) / 3, result.Progress!.Value, 6);
        await using var db = _database.NewContext();
        Assert.Equal(1, await db.Projects.CountAsync());
        Assert.Equal(3, await db.Goals.CountAsync());
    }

    [Fact]
    public async Task LaterRun_CanAddAComponentAndANewMetricToAnExistingProject()
    {
        await IngestAsync(Request(coverage: 60, bugs: 4));
        var request = Request(coverage: 60, bugs: 4);
        request.Components![0].Metrics!["vulnerabilities"] = 2;
        request.Components.Add(new IngestComponent { Key = "frontend", Metrics = new() { ["coverage"] = 20 } });

        await IngestAsync(request);

        await using var db = _database.NewContext();
        Assert.Equal(["backend", "frontend"], (await db.Components.ToListAsync()).Select(c => c.Key).Order());
        // The three of the first run, plus vulnerabilities on the backend and coverage on the frontend.
        Assert.Equal(5, await db.Goals.CountAsync());
    }

    [Fact]
    public async Task PreviousRun_IsTakenFromTheSameBranchOnly()
    {
        await IngestAsync(Request(coverage: 60, bugs: 4, branch: "main"));

        var result = await IngestAsync(Request(coverage: 70, bugs: 2, branch: "feature/x"));

        Assert.Null(result.PreviousProgress);
    }

    [Fact]
    public async Task RemovedGoal_IsNotRecreatedByLaterRuns()
    {
        await IngestAsync(Request(coverage: 60, bugs: 4));
        await using (var db = _database.NewContext())
        {
            var componentId = (await db.Components.SingleAsync()).Id;
            await new GoalService(db, _clock).RemoveAsync(_organizationId, "shop", componentId, "bugs");
        }

        await IngestAsync(Request(coverage: 61, bugs: 4));

        await using var check = _database.NewContext();
        Assert.DoesNotContain(await check.Goals.ToListAsync(), g => g.MetricKey == "bugs");
    }

    [Fact]
    public async Task NewGoal_TakesTheLatestValueAsBaseline()
    {
        await IngestAsync(Request(coverage: 60, bugs: 4));
        await using var db = _database.NewContext();
        var componentId = (await db.Components.SingleAsync()).Id;
        var goals = new GoalService(db, _clock);

        await goals.SetAsync(_organizationId, "shop", componentId, "tests_total", 50);
        await goals.SetAsync(_organizationId, "shop", componentId, "coverage", 90);

        var saved = await db.Goals.AsNoTracking().ToListAsync();
        Assert.Equal((10d, 50d), saved.Where(g => g.MetricKey == "tests_total").Select(g => (g.Baseline, g.Target)).Single());
        // An existing goal keeps its baseline when only the target changes.
        Assert.Equal((60d, 90d), saved.Where(g => g.MetricKey == "coverage").Select(g => (g.Baseline, g.Target)).Single());
    }

    [Fact]
    public async Task InvalidRequest_IsRejectedWithEveryProblem()
    {
        var request = new IngestRunRequest
        {
            Project = "Not valid!",
            Branch = " ",
            Components = [new IngestComponent { Key = "backend" }, new IngestComponent { Key = "backend", Metrics = new() { ["Bad Key"] = 1 } }]
        };

        var error = await Assert.ThrowsAsync<ValidationException>(() => IngestAsync(request));

        Assert.Equal(5, error.Errors.Count);
        await using var db = _database.NewContext();
        Assert.Equal(0, await db.Projects.CountAsync());
    }

    [Fact]
    public async Task Dashboard_ComparesTheLatestRunWithThePreviousAndIsolatesTenants()
    {
        await IngestAsync(Request(coverage: 60, bugs: 4));
        await IngestAsync(Request(coverage: 70, bugs: 5, failed: 1));
        var otherOrganization = _database.AddOrganization("Otra");
        await IngestAsync(Request(coverage: 10, bugs: 99), otherOrganization);

        await using var db = _database.NewContext();
        var service = new DashboardService(db);
        var dashboard = await service.GetProjectAsync(_organizationId, "shop", branch: null, runId: null);

        Assert.NotNull(dashboard);
        Assert.Equal(2, dashboard.History.Count);
        var metrics = Assert.Single(dashboard.Components).Metrics.ToDictionary(m => m.Metric.Key);
        Assert.Equal((70d, 60d, Trend.Improved), (metrics["coverage"].Current, metrics["coverage"].Previous, metrics["coverage"].Trend));
        Assert.Equal(Trend.Worsened, metrics["bugs"].Trend);
        Assert.Equal(Trend.Worsened, metrics["tests_failed"].Trend);
        Assert.Equal(Trend.Unchanged, metrics["bundle_size_kb"].Trend);
        Assert.Equal(0.5, metrics["coverage"].Goal!.Progress, 6);
        // coverage 50 %, bugs worse than baseline 0 %, failed tests regressed from a met target 0 %.
        Assert.Equal(0.5 / 3, dashboard.Progress!.Value, 6);
        Assert.Equal(1 / 3d, dashboard.PreviousProgress!.Value, 6);
        Assert.Equal([60d, 70d], dashboard.Trends.Single(t => t.Metric.Key == "coverage").Points.Select(p => p.Value));

        Assert.Equal(1, Assert.Single(await service.GetOverviewAsync(otherOrganization)).RunCount);
        Assert.Null(await service.GetProjectAsync(otherOrganization, "missing", null, null));
        Assert.Equal(10d, (await service.GetProjectAsync(otherOrganization, "shop", null, null))!
            .Components[0].Metrics.Single(m => m.Metric.Key == "coverage").Current);
    }
}
