namespace QualityTrack.Application.Common;

/// <summary>The request is well-formed but breaks a rule; the messages are safe to show to the caller.</summary>
public class ValidationException(IReadOnlyList<string> errors) : Exception(string.Join(" ", errors))
{
    public ValidationException(string error) : this([error]) { }

    public IReadOnlyList<string> Errors { get; } = errors;
}
