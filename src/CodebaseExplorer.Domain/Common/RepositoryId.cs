namespace CodebaseExplorer.Domain.Common;

/// <summary>
/// Identity of a registered repository. Random (UUID v7, time-ordered); identifies the user's
/// registration of a repository, not its content (DOMAIN_MODEL.md §3.1).
/// </summary>
public readonly record struct RepositoryId
{
    public RepositoryId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A repository id must not be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    /// <summary>Creates a new time-ordered (UUID v7) identifier.</summary>
    public static RepositoryId New() => new(Guid.CreateVersion7());

    /// <summary>Creates a new time-ordered (UUID v7) identifier for the given timestamp.</summary>
    public static RepositoryId New(DateTimeOffset timestamp) => new(Guid.CreateVersion7(timestamp));

    /// <summary>32 lowercase hexadecimal characters; this form is used inside natural keys.</summary>
    public override string ToString() => Value.ToString("N");
}
