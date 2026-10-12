namespace QualityTrack.Domain.Entities;

/// <summary>An application whose quality is tracked (e.g. "online-shop").</summary>
public class Solution
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    /// <summary>Stable identifier sent by the reporter; unique inside the organization.</summary>
    public required string Key { get; set; }
    public required string Name { get; set; }
    public DateTime CreatedAt { get; set; }

    public Organization? Organization { get; set; }
    public List<Project> Projects { get; set; } = [];
    public List<Run> Runs { get; set; } = [];
    public List<Goal> Goals { get; set; } = [];
}
