namespace QualityTrack.Domain.Entities;

/// <summary>Target for a metric of a project; progress is measured from <see cref="Baseline"/> to <see cref="Target"/>.</summary>
public class Goal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SolutionId { get; set; }
    public Guid ProjectId { get; set; }
    public required string MetricKey { get; set; }
    /// <summary>Value the metric had when the goal was set.</summary>
    public double Baseline { get; set; }
    public double Target { get; set; }
    public DateTime CreatedAt { get; set; }

    public Solution? Solution { get; set; }
    public Project? Project { get; set; }
}
