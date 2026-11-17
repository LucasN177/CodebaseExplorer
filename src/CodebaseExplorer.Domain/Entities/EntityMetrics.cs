namespace CodebaseExplorer.Domain.Entities;

/// <summary>Cheap, syntactic size metrics of a code element.</summary>
public sealed record EntityMetrics
{
    public EntityMetrics(int? lineCount, int? memberCount)
    {
        if (lineCount is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lineCount), lineCount, "Must not be negative.");
        }

        if (memberCount is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(memberCount), memberCount, "Must not be negative.");
        }

        LineCount = lineCount;
        MemberCount = memberCount;
    }

    public int? LineCount { get; }

    public int? MemberCount { get; }
}
