namespace QualityTrack.Application.Ingestion;

/// <summary>Payload of <c>POST /api/v1/runs</c>.</summary>
public sealed record IngestRunRequest
{
    /// <summary>Stable project key, e.g. "clinoft". The project is created on its first run.</summary>
    public string? Project { get; init; }
    public string? ProjectName { get; init; }
    public string? Branch { get; init; }
    public string? Commit { get; init; }
    public bool Dirty { get; init; }
    public string? Source { get; init; }
    /// <summary>When the run happened; defaults to the moment it is received.</summary>
    public DateTimeOffset? ExecutedAt { get; init; }
    public List<IngestComponent>? Components { get; init; }
}

public sealed record IngestComponent
{
    public string? Key { get; init; }
    public string? Name { get; init; }
    public IngestTests? Tests { get; init; }
    /// <summary>Metric key → value. Keys from the catalog get names, units and goals; any other key is stored as-is.</summary>
    public Dictionary<string, double>? Metrics { get; init; }
}

public sealed record IngestTests
{
    public int Total { get; init; }
    public int Passed { get; init; }
    public int Failed { get; init; }
    public int Skipped { get; init; }
    public double? DurationSeconds { get; init; }
}

/// <param name="Progress">Progress toward the project's goals after this run (0..1), if it has goals.</param>
/// <param name="PreviousProgress">Same figure for the previous run on the branch, if any.</param>
public sealed record IngestRunResult(Guid RunId, string Project, string Branch, bool ProjectCreated, double? Progress, double? PreviousProgress);
