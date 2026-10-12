namespace QualityTrack.Domain.Entities;

/// <summary>Tenant: every project, user and API key belongs to exactly one organization.</summary>
public class Organization
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public DateTime CreatedAt { get; set; }

    public List<User> Users { get; set; } = [];
    public List<Project> Projects { get; set; } = [];
    public List<ApiKey> ApiKeys { get; set; } = [];
}
