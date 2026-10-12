using QualityTrack.Application.Dashboard;
using QualityTrack.Domain.Metrics;
using QualityTrack.Domain.Progress;

namespace QualityTrack.Application.Ingestion;

/// <summary>Payload of <c>POST /api/v1/runs</c>.</summary>
public sealed record IngestRunRequest
{
    /// <summary>Stable solution key, e.g. "online-shop". The solution is created on its first run.</summary>
    public string? Solution { get; init; }
    public string? SolutionName { get; init; }
    public string? Branch { get; init; }
    public string? Commit { get; init; }
    public bool Dirty { get; init; }
    public string? Source { get; init; }
    /// <summary>When the run happened; defaults to the moment it is received.</summary>
    public DateTimeOffset? ExecutedAt { get; init; }
    public List<IngestProject>? Projects { get; init; }
    /// <summary>Rules referenced by the findings: what each one means and how to fix it.</summary>
    public List<IngestRule>? Rules { get; init; }
}

public sealed record IngestRule
{
    public string? Key { get; init; }
    public string? Name { get; init; }
    /// <summary>Plain text. HTML is not rendered.</summary>
    public string? Description { get; init; }
    public string? HowToFix { get; init; }
}

/// <summary>A concrete problem at a place in the code, with the lines around it.</summary>
public sealed record IngestFinding
{
    /// <summary>Metric the finding is counted in: "bugs", "vulnerabilities", "code_smells"…</summary>
    public string? Metric { get; init; }
    public string? Rule { get; init; }
    public string? Severity { get; init; }
    public string? Message { get; init; }
    /// <summary>Path relative to the repository root.</summary>
    public string? File { get; init; }
    public int? Line { get; init; }
    public int? EndLine { get; init; }
    public int? StartColumn { get; init; }
    public int? EndColumn { get; init; }
    public IngestSnippet? Snippet { get; init; }
}

public sealed record IngestSnippet
{
    public int StartLine { get; init; }
    public List<string>? Lines { get; init; }
}

public sealed record IngestProject
{
    public string? Key { get; init; }
    public string? Name { get; init; }
    public IngestTests? Tests { get; init; }
    /// <summary>Metric key → value. Keys from the catalog get names, units and goals; any other key is stored as-is.</summary>
    public Dictionary<string, double>? Metrics { get; init; }
    /// <summary>Optional detail behind the counts. Replaces the findings stored for the branch.</summary>
    public List<IngestFinding>? Findings { get; init; }
}

public sealed record IngestTests
{
    public int Total { get; init; }
    public int Passed { get; init; }
    public int Failed { get; init; }
    public int Skipped { get; init; }
    public double? DurationSeconds { get; init; }
}

/// <param name="Progress">Progress toward the solution's goals after this run (0..1), if it has goals.</param>
/// <param name="PreviousProgress">Same figure for the previous run on the branch, if any.</param>
/// <param name="Projects">What the run reported, compared with the previous run on the branch.</param>
public sealed record IngestRunResult(
    Guid RunId,
    string Solution,
    string Branch,
    bool SolutionCreated,
    double? Progress,
    double? PreviousProgress,
    IReadOnlyList<IngestedProject> Projects);

public sealed record IngestedProject(string Key, string Name, IReadOnlyList<IngestedMetric> Metrics);

/// <param name="Previous">Value in the previous run on the branch; null if it did not report the metric.</param>
/// <param name="Trend">Whether the change from <paramref name="Previous"/> is good, bad or indifferent.</param>
/// <param name="Goal">The metric's goal and how far along it is, if it has one.</param>
public sealed record IngestedMetric(
    string Key,
    string Name,
    MetricCategory Category,
    MetricUnit Unit,
    double Current,
    double? Previous,
    double? Delta,
    Trend Trend,
    GoalStatus? Goal);
