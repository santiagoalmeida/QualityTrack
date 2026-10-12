namespace QualityTrack.Domain.Entities;

/// <summary>A separately measured part of a project (e.g. "backend", "frontend").</summary>
public class Component
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public required string Key { get; set; }
    public required string Name { get; set; }

    public Project? Project { get; set; }
}
