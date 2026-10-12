namespace QualityTrack.Domain.Entities;

/// <summary>What a rule means and how to fix what it reports. Shared by every finding of the rule in a solution.</summary>
public class Rule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SolutionId { get; set; }
    /// <summary>Analyzer's identifier, e.g. "csharpsquid:S1656".</summary>
    public required string Key { get; set; }
    public required string Name { get; set; }
    /// <summary>Why it is a problem, as plain text.</summary>
    public string? Description { get; set; }
    /// <summary>How to fix it, as plain text.</summary>
    public string? HowToFix { get; set; }

    public Solution? Solution { get; set; }
}
