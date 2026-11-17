namespace CodebaseExplorer.Domain.Model;

/// <summary>Thrown by <see cref="ArchitectureModel.EnsureValid"/>; carries all violations.</summary>
public sealed class ModelValidationException : Exception
{
    public ModelValidationException()
        : this([])
    {
    }

    public ModelValidationException(string message)
        : base(message)
    {
        Violations = [];
    }

    public ModelValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
        Violations = [];
    }

    public ModelValidationException(IReadOnlyList<ModelViolation> violations)
        : base(BuildMessage(violations))
    {
        Violations = violations;
    }

    public IReadOnlyList<ModelViolation> Violations { get; }

    private static string BuildMessage(IReadOnlyList<ModelViolation> violations)
    {
        ArgumentNullException.ThrowIfNull(violations);

        const int shown = 10;
        var lines = violations.Take(shown).Select(v => "  " + v);
        var more = violations.Count > shown ? $"{Environment.NewLine}  … and {violations.Count - shown} more" : string.Empty;

        return $"The architecture model has {violations.Count} violation(s):{Environment.NewLine}"
            + string.Join(Environment.NewLine, lines) + more;
    }
}
