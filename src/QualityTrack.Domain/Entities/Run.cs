namespace QualityTrack.Domain.Entities;

/// <summary>One execution of tests and analysis on a branch.</summary>
public class Run
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public required string Branch { get; set; }
    public string? CommitSha { get; set; }
    /// <summary>True when the working tree had uncommitted changes.</summary>
    public bool IsDirty { get; set; }
    /// <summary>Who reported it (e.g. "local", "ci").</summary>
    public string? Source { get; set; }
    public DateTime ExecutedAt { get; set; }

    public Project? Project { get; set; }
    public List<Measurement> Measurements { get; set; } = [];
}
