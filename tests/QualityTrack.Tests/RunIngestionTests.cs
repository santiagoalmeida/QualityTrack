using Microsoft.EntityFrameworkCore;
using QualityTrack.Application.Common;
using QualityTrack.Application.Dashboard;
using QualityTrack.Application.Findings;
using QualityTrack.Domain.Metrics;
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
        Solution = "Shop",
        SolutionName = "Tienda",
        Branch = branch,
        Commit = "abc1234def",
        Projects =
        [
            new IngestProject
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
    public async Task FirstRun_CreatesSolutionProjectMeasurementsAndDefaultGoals()
    {
        var result = await IngestAsync(Request(coverage: 60, bugs: 4));

        await using var db = _database.NewContext();
        var solution = await db.Solutions.Include(p => p.Projects).Include(p => p.Goals).SingleAsync();
        Assert.True(result.SolutionCreated);
        Assert.Equal(("shop", "Tienda"), (solution.Key, solution.Name));
        Assert.Equal("Backend", Assert.Single(solution.Projects).Name);
        // 4 test counts + 3 metrics.
        Assert.Equal(7, await db.Measurements.CountAsync());
        Assert.Equal(
            [("bugs", 4d, 0d), ("coverage", 60d, 80d), ("tests_failed", 0d, 0d)],
            solution.Goals.OrderBy(g => g.MetricKey).Select(g => (g.MetricKey, g.Baseline, g.Target)));
        // coverage 0 %, bugs 0 %, failed tests already at target.
        Assert.Equal(1 / 3d, result.Progress!.Value, 6);
        Assert.Null(result.PreviousProgress);
    }

    [Fact]
    public async Task SecondRun_ReportsProgressAgainstThePreviousOne()
    {
        await IngestAsync(Request(coverage: 60, bugs: 4));

        var result = await IngestAsync(Request(coverage: 70, bugs: 0));

        Assert.False(result.SolutionCreated);
        var metrics = Assert.Single(result.Projects).Metrics.ToDictionary(m => m.Key);
        Assert.Equal((70d, 60d, 10d, Trend.Improved), (metrics["coverage"].Current, metrics["coverage"].Previous, metrics["coverage"].Delta, metrics["coverage"].Trend));
        Assert.Equal((Trend.Improved, true), (metrics["bugs"].Trend, metrics["bugs"].Goal!.Met));
        Assert.Null(metrics["bundle_size_kb"].Goal);
        Assert.Equal(1 / 3d, result.PreviousProgress!.Value, 6);
        // coverage halfway, bugs done, failed tests done.
        Assert.Equal((0.5 + 1 + 1) / 3, result.Progress!.Value, 6);
        await using var db = _database.NewContext();
        Assert.Equal(1, await db.Solutions.CountAsync());
        Assert.Equal(3, await db.Goals.CountAsync());
    }

    [Fact]
    public async Task LaterRun_CanAddAProjectAndANewMetricToAnExistingSolution()
    {
        await IngestAsync(Request(coverage: 60, bugs: 4));
        var request = Request(coverage: 60, bugs: 4);
        request.Projects![0].Metrics!["vulnerabilities"] = 2;
        request.Projects.Add(new IngestProject { Key = "frontend", Metrics = new() { ["coverage"] = 20 } });

        await IngestAsync(request);

        await using var db = _database.NewContext();
        Assert.Equal(["backend", "frontend"], (await db.Projects.ToListAsync()).Select(c => c.Key).Order());
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
            var projectId = (await db.Projects.SingleAsync()).Id;
            await new GoalService(db, _clock).RemoveAsync(_organizationId, "shop", projectId, "bugs");
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
        var projectId = (await db.Projects.SingleAsync()).Id;
        var goals = new GoalService(db, _clock);

        await goals.SetAsync(_organizationId, "shop", projectId, "tests_total", 50);
        await goals.SetAsync(_organizationId, "shop", projectId, "coverage", 90);

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
            Solution = "Not valid!",
            Branch = " ",
            Projects = [new IngestProject { Key = "backend" }, new IngestProject { Key = "backend", Metrics = new() { ["Bad Key"] = 1 } }]
        };

        var error = await Assert.ThrowsAsync<ValidationException>(() => IngestAsync(request));

        Assert.Equal(5, error.Errors.Count);
        await using var db = _database.NewContext();
        Assert.Equal(0, await db.Solutions.CountAsync());
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
        var dashboard = await service.GetSolutionAsync(_organizationId, "shop", branch: null, runId: null);

        Assert.NotNull(dashboard);
        Assert.Equal(2, dashboard.History.Count);
        var metrics = Assert.Single(dashboard.Projects).Metrics.ToDictionary(m => m.Metric.Key);
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
        Assert.Null(await service.GetSolutionAsync(otherOrganization, "missing", null, null));
        Assert.Equal(10d, (await service.GetSolutionAsync(otherOrganization, "shop", null, null))!
            .Projects[0].Metrics.Single(m => m.Metric.Key == "coverage").Current);
    }

    private static IngestRunRequest WithFindings(IngestRunRequest request, params (string Rule, string File, int Line)[] smells)
    {
        request.Projects![0].Findings.AddRange(smells.Select(smell => new IngestFinding
        {
            Metric = "code_smells",
            Rule = smell.Rule,
            Severity = "MAJOR",
            Message = $"Fix {smell.Rule}",
            File = smell.File,
            Line = smell.Line,
            StartColumn = 4,
            EndColumn = 9,
            Snippet = new IngestSnippet { StartLine = smell.Line - 1, Lines = ["before", "    wrong();", "after"] }
        }));
        return request;
    }

    private static IngestRunRequest RequestWithFindings(string branch = "main")
    {
        var request = Request(coverage: 60, bugs: 4, branch: branch);
        request.Projects![0] = request.Projects[0] with { Findings = [] };
        return request with
        {
            Rules = [new IngestRule { Key = "cs:S1", Name = "No self-assignment", Description = "Why", HowToFix = "Remove it" }]
        };
    }

    [Fact]
    public async Task Findings_AreGroupedByRuleAndShownWithTheirCode()
    {
        await IngestAsync(WithFindings(RequestWithFindings(), ("cs:S1", "src/B.cs", 20), ("cs:S1", "src/A.cs", 7), ("cs:S2", "src/A.cs", 3)));

        await using var db = _database.NewContext();
        var service = new FindingsService(db);
        var rules = await service.GetRulesAsync(_organizationId, "shop", "backend", "code_smells", branch: null, runId: null);

        Assert.NotNull(rules);
        Assert.Equal(3, rules.Total);
        // Most frequent first; a rule without metadata is named by its key.
        Assert.Equal(
            [("cs:S1", "No self-assignment", 2, 2, "major"), ("cs:S2", "cs:S2", 1, 1, "major")],
            rules.Rules.Select(r => (r.Key, r.Name, r.Count, r.Files, r.TopSeverity!)));

        var rule = await service.GetRuleFindingsAsync(_organizationId, "shop", "backend", "code_smells", "cs:S1", null, null);
        Assert.NotNull(rule);
        Assert.Equal(("No self-assignment", "Why", "Remove it", 2), (rule.RuleName, rule.Description, rule.HowToFix, rule.Total));
        var first = rule.Findings[0];
        Assert.Equal(("src/A.cs", 7, 7, 4, 9, 6), (first.FilePath, first.Line, first.EndLine, first.StartColumn, first.EndColumn, first.SnippetStartLine));
        Assert.Equal(["before", "    wrong();", "after"], first.SnippetLines);

        var dashboard = await new DashboardService(db).GetSolutionAsync(_organizationId, "shop", null, null);
        var metrics = dashboard!.Projects[0].Metrics.ToDictionary(m => m.Metric.Key);
        Assert.False(metrics["bugs"].HasFindings);

        Assert.Null(await service.GetRuleFindingsAsync(_organizationId, "shop", "backend", "code_smells", "cs:missing", null, null));
        Assert.Null(await service.GetRulesAsync(_database.AddOrganization("Otra"), "shop", "backend", "code_smells", null, null));
    }

    [Fact]
    public async Task Findings_AreKeptOnlyForTheLatestRunOfEachBranch()
    {
        var first = await IngestAsync(WithFindings(RequestWithFindings(), ("cs:S1", "src/A.cs", 7), ("cs:S1", "src/B.cs", 9)));
        await IngestAsync(WithFindings(RequestWithFindings("feature/x"), ("cs:S1", "src/A.cs", 7)));

        // A later run on main that reports findings replaces the ones of the first; the other branch keeps its own.
        var second = await IngestAsync(WithFindings(RequestWithFindings(), ("cs:S1", "src/A.cs", 8)));

        await using var db = _database.NewContext();
        Assert.Equal(0, await db.Findings.CountAsync(f => f.RunId == first.RunId));
        Assert.Equal(1, await db.Findings.CountAsync(f => f.RunId == second.RunId));
        Assert.Equal(2, await db.Findings.CountAsync());
        Assert.Equal(1, await db.Rules.CountAsync());

        // A run without findings (numbers only) leaves the stored detail alone.
        await IngestAsync(Request(coverage: 61, bugs: 4));

        await using var check = _database.NewContext();
        Assert.Equal(1, await check.Findings.CountAsync(f => f.RunId == second.RunId));
    }

    [Fact]
    public async Task Findings_WithoutTheirRequiredFieldsAreRejected()
    {
        var request = RequestWithFindings();
        request.Projects![0].Findings!.Add(new IngestFinding { Metric = "code_smells", Rule = " ", Message = "", File = "a.cs", Line = 5, EndLine = 2 });

        var error = await Assert.ThrowsAsync<ValidationException>(() => IngestAsync(request));

        Assert.Equal(3, error.Errors.Count);
    }

    [Fact]
    public void FixPrompt_NamesTheRuleTheLocationsAndAdaptsToEachAssistant()
    {
        var context = new FindingsContext("shop", "Tienda", "backend", "Backend", MetricCatalog.Get("code_smells"),
            new RunInfo(Guid.NewGuid(), "main", "abc1234", false, null, DateTime.UtcNow));
        FindingDetail At(string file, int line) => new("Remove this", "major", file, line, line, null, null, ["a = 1;", "a = a;", "b = 2;"], line - 1);
        var rule = new RuleFindings(context, "cs:S1", "No self-assignment", "It does nothing.", "Delete the line.", 45,
            Enumerable.Range(1, 45).Select(i => At($"src/F{i}.cs", 10)).ToList());

        var claude = FixPromptBuilder.ForRule(AiAssistant.Claude, rule);
        var copilot = FixPromptBuilder.ForRule(AiAssistant.Copilot, rule);
        var single = FixPromptBuilder.ForFinding(AiAssistant.Codex, rule, rule.Findings[0]);

        Assert.Contains("«No self-assignment» (cs:S1)", claude);
        Assert.Contains("las 45 incidencias", claude);
        Assert.Contains("Delete the line.", claude);
        Assert.Contains("- src/F1.cs:10 — Remove this", claude);
        Assert.Contains($"- src/F{FixPromptBuilder.MaxLocations}.cs:10", claude);
        Assert.DoesNotContain($"src/F{FixPromptBuilder.MaxLocations + 1}.cs", claude);
        Assert.Contains("y 5 más de la misma regla", claude);
        Assert.StartsWith("#file:src/F1.cs #file:src/F2.cs", copilot);
        Assert.DoesNotContain("#file:", claude);
        Assert.Contains(">   10 | a = a;", single);
        Assert.Contains("     9 | a = 1;", single);
        Assert.Contains("muestra el diff final", single);
    }
}
