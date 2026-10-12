namespace QualityTrack.Domain.Entities;

/// <summary>
/// One concrete problem behind a count: the bug, vulnerability or code smell an analyzer found at a
/// place in the code. Kept only for the latest run of each branch, because it carries source code.
/// </summary>
public class Finding
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RunId { get; set; }
    public Guid ProjectId { get; set; }
    /// <summary>Metric this finding is counted in, e.g. "code_smells".</summary>
    public required string MetricKey { get; set; }
    public required string RuleKey { get; set; }
    public string? Severity { get; set; }
    public required string Message { get; set; }
    /// <summary>Path relative to the repository root.</summary>
    public required string FilePath { get; set; }
    /// <summary>First line to fix (1-based); null when the finding is about the whole file.</summary>
    public int? Line { get; set; }
    public int? EndLine { get; set; }
    /// <summary>0-based column where the marked range starts on <see cref="Line"/>.</summary>
    public int? StartColumn { get; set; }
    /// <summary>0-based column where the marked range ends on <see cref="EndLine"/> (exclusive).</summary>
    public int? EndColumn { get; set; }
    /// <summary>Source lines around the finding, joined with '\n'.</summary>
    public string? Snippet { get; set; }
    /// <summary>Line number of the first line of <see cref="Snippet"/>.</summary>
    public int? SnippetStartLine { get; set; }

    public Run? Run { get; set; }
    public Project? Project { get; set; }
}
