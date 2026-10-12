namespace QualityTrack.Domain.Entities;

/// <summary>A separately measured part of a solution (e.g. "backend", "frontend").</summary>
public class Project
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SolutionId { get; set; }
    public required string Key { get; set; }
    public required string Name { get; set; }

    public Solution? Solution { get; set; }
}
