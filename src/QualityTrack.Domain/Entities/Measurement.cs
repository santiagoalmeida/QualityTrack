namespace QualityTrack.Domain.Entities;

/// <summary>Value of one metric for one project in one run. Stored "tall" so new metrics need no schema change.</summary>
public class Measurement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RunId { get; set; }
    public Guid ProjectId { get; set; }
    public required string MetricKey { get; set; }
    public double Value { get; set; }

    public Run? Run { get; set; }
    public Project? Project { get; set; }
}
