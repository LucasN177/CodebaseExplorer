namespace CodebaseExplorer.Domain.Common;

/// <summary>Identity of one analysis run (UUID v7, time-ordered).</summary>
public readonly record struct AnalysisRunId
{
    public AnalysisRunId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An analysis run id must not be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    /// <summary>Creates a new time-ordered (UUID v7) identifier.</summary>
    public static AnalysisRunId New() => new(Guid.CreateVersion7());

    /// <summary>Creates a new time-ordered (UUID v7) identifier for the given timestamp.</summary>
    public static AnalysisRunId New(DateTimeOffset timestamp) => new(Guid.CreateVersion7(timestamp));

    /// <summary>32 lowercase hexadecimal characters.</summary>
    public override string ToString() => Value.ToString("N");
}
