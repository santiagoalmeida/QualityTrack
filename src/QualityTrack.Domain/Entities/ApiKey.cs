namespace QualityTrack.Domain.Entities;

/// <summary>Credential used by CI or scripts to report runs. Only the hash is stored.</summary>
public class ApiKey
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public required string Name { get; set; }
    /// <summary>First characters of the key, kept in clear so the user can tell keys apart.</summary>
    public required string Prefix { get; set; }
    public required string KeyHash { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    public Organization? Organization { get; set; }
}
