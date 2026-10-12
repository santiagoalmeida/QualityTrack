namespace QualityTrack.Domain.Entities;

/// <summary>Tenant: every solution, user and API key belongs to exactly one organization.</summary>
public class Organization
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public DateTime CreatedAt { get; set; }

    public List<User> Users { get; set; } = [];
    public List<Solution> Solutions { get; set; } = [];
    public List<ApiKey> ApiKeys { get; set; } = [];
}
