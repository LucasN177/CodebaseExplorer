namespace CodebaseExplorer.Application.Git;

/// <summary>
/// Bounds the history walk of <see cref="IGitRepositoryReader.ReadFileActivityAsync"/> (GIT_INTEGRATION.md §2): at most
/// <see cref="MaxCommits"/> commits, none committed before <see cref="Since"/>.
/// </summary>
public sealed record GitHistoryWindow
{
    /// <summary>Default maximum number of commits.</summary>
    public const int DefaultMaxCommits = 5_000;

    /// <summary>Default look-back period.</summary>
    public static readonly TimeSpan DefaultPeriod = TimeSpan.FromDays(365);

    public GitHistoryWindow(int maxCommits, DateTimeOffset? since)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCommits);
        MaxCommits = maxCommits;
        Since = since;
    }

    /// <summary>Maximum number of (non-merge) commits whose changes are counted.</summary>
    public int MaxCommits { get; }

    /// <summary>Oldest commit time (committer date) to include; <see langword="null"/> for no time bound.</summary>
    public DateTimeOffset? Since { get; }

    /// <summary>The default window: 5,000 commits of the last 365 days before <paramref name="now"/>.</summary>
    public static GitHistoryWindow Default(DateTimeOffset now) => new(DefaultMaxCommits, now - DefaultPeriod);
}
