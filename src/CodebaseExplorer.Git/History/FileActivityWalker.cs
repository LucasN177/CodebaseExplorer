using CodebaseExplorer.Application.Git;
using LibGit2Sharp;

namespace CodebaseExplorer.Git.History;

/// <summary>
/// Single-pass history walk for <see cref="IGitRepositoryReader.ReadFileActivityAsync"/> (GIT-004). Selecting the
/// commits is cheap; diffing them with line counts dominates (GIT_INTEGRATION.md §3.1), so the selected commits are
/// diffed in parallel, each worker with its own <see cref="Repository"/> instance (libgit2 objects are not shared
/// across threads), and the per-worker results are merged.
/// </summary>
internal static class FileActivityWalker
{
    private static readonly CompareOptions DiffOptions = new()
    {
        // No rename detection: matches `git log --no-renames --numstat` and is faster (GIT-006).
        Similarity = SimilarityOptions.None,
        IncludeUnmodified = false,
    };

    public static GitFileActivityReport Walk(
        GitRepositoryInfo info, GitHistoryWindow window, IProgress<GitProgress>? progress, CancellationToken cancellationToken)
    {
        var (commits, reachedLimit) = SelectCommits(info, window, cancellationToken);
        progress?.Report(new GitProgress(GitProgressStage.ReadingHistory, 0, commits.Count));

        var prefix = info.RootPrefix.Length == 0 ? string.Empty : info.RootPrefix + "/";
        var workers = Math.Clamp(Environment.ProcessorCount, 1, 8);
        var partials = new FileStatistics[workers];
        var completed = 0L;

        try
        {
            Parallel.For(0, workers, new ParallelOptions { MaxDegreeOfParallelism = workers, CancellationToken = cancellationToken }, worker =>
            {
                var statistics = new FileStatistics();
                using var repository = new Repository(info.WorkingDirectory);
                for (var i = worker; i < commits.Count; i += workers)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Accumulate(repository, commits[i], prefix, statistics);
                    var done = Interlocked.Increment(ref completed);
                    if (done % 100 == 0)
                    {
                        progress?.Report(new GitProgress(GitProgressStage.ReadingHistory, done, commits.Count));
                    }
                }

                partials[worker] = statistics;
            });
        }
        catch (AggregateException exception) when (exception.InnerExceptions.FirstOrDefault(e => e is LibGit2SharpException) is { } gitException)
        {
            // Callers translate Git failures into results; unwrap the first one.
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(gitException).Throw();
            throw;
        }

        var merged = new FileStatistics();
        foreach (var partial in partials)
        {
            merged.MergeFrom(partial);
        }

        progress?.Report(new GitProgress(GitProgressStage.ReadingHistory, commits.Count, commits.Count));
        return new GitFileActivityReport(
            merged.ToActivities(),
            commits.Count,
            commits.Count == 0 ? null : commits.Min(c => c.CommittedAt),
            reachedLimit);
    }

    private static (List<SelectedCommit> Commits, bool ReachedLimit) SelectCommits(
        GitRepositoryInfo info, GitHistoryWindow window, CancellationToken cancellationToken)
    {
        var selected = new List<SelectedCommit>();
        using var repository = new Repository(info.WorkingDirectory);
        if (repository.Head.Tip is null)
        {
            return (selected, false);
        }

        var shallow = repository.Info.IsShallow;
        var filter = new CommitFilter { IncludeReachableFrom = repository.Head, SortBy = CommitSortStrategies.Time };
        foreach (var commit in repository.Commits.QueryBy(filter))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var committedAt = commit.Committer.When;
            if (window.Since is { } since && committedAt < since)
            {
                // Time order: everything after this commit is older (clock skew aside).
                break;
            }

            var parents = commit.Parents.Take(2).ToList();
            if (parents.Count > 1 || (shallow && parents.Count == 0))
            {
                continue;
            }

            if (selected.Count == window.MaxCommits)
            {
                return (selected, true);
            }

            selected.Add(new SelectedCommit(commit.Sha, parents.FirstOrDefault()?.Sha, committedAt, AuthorKey(commit.Author)));
        }

        return (selected, false);
    }

    private static void Accumulate(Repository repository, SelectedCommit selected, string prefix, FileStatistics statistics)
    {
        var tree = repository.Lookup<Commit>(selected.Sha).Tree;
        var parentTree = selected.ParentSha is null ? null : repository.Lookup<Commit>(selected.ParentSha).Tree;
        // For a subfolder, the pathspec keeps libgit2 from diffing the rest of the repository (the prefix filter below
        // stays authoritative).
        using var patch = prefix.Length > 0
            ? repository.Diff.Compare<Patch>(parentTree, tree, [prefix.TrimEnd('/')], null, DiffOptions)
            : repository.Diff.Compare<Patch>(parentTree, tree, DiffOptions);
        foreach (var change in patch)
        {
            var path = change.Path.Replace('\\', '/');
            if (prefix.Length > 0)
            {
                if (!path.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                path = path[prefix.Length..];
            }

            statistics.Add(path, selected, change.LinesAdded, change.LinesDeleted);
        }
    }

    private static string AuthorKey(Signature author) =>
        string.IsNullOrWhiteSpace(author.Email) ? "name:" + author.Name.Trim() : author.Email.Trim().ToLowerInvariant();

    private sealed record SelectedCommit(string Sha, string? ParentSha, DateTimeOffset CommittedAt, string AuthorKey);

    /// <summary>Per-file accumulator; author keys live only for the duration of the walk.</summary>
    private sealed class FileStatistics
    {
        private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

        public void Add(string path, SelectedCommit commit, int linesAdded, int linesDeleted)
        {
            if (!_entries.TryGetValue(path, out var entry))
            {
                entry = new Entry(commit.CommittedAt, commit.CommittedAt, commit.Sha);
                _entries[path] = entry;
            }

            entry.Commits++;
            entry.Authors.Add(commit.AuthorKey);
            entry.LinesAdded += linesAdded;
            entry.LinesDeleted += linesDeleted;
            entry.Include(commit.CommittedAt, commit.CommittedAt, commit.Sha);
        }

        public void MergeFrom(FileStatistics other)
        {
            foreach (var (path, theirs) in other._entries)
            {
                if (!_entries.TryGetValue(path, out var entry))
                {
                    _entries[path] = theirs;
                    continue;
                }

                entry.Commits += theirs.Commits;
                entry.Authors.UnionWith(theirs.Authors);
                entry.LinesAdded += theirs.LinesAdded;
                entry.LinesDeleted += theirs.LinesDeleted;
                entry.Include(theirs.First, theirs.Last, theirs.LastSha);
            }
        }

        public List<GitFileActivity> ToActivities() =>
        [
            .. _entries
                .OrderBy(e => e.Key, StringComparer.Ordinal)
                .Select(e => new GitFileActivity(
                    e.Key, e.Value.Commits, e.Value.Authors.Count, e.Value.LinesAdded, e.Value.LinesDeleted,
                    e.Value.First, e.Value.Last, e.Value.LastSha)),
        ];

        private sealed class Entry(DateTimeOffset first, DateTimeOffset last, string lastSha)
        {
            public int Commits { get; set; }

            public HashSet<string> Authors { get; } = new(StringComparer.Ordinal);

            public long LinesAdded { get; set; }

            public long LinesDeleted { get; set; }

            public DateTimeOffset First { get; private set; } = first;

            public DateTimeOffset Last { get; private set; } = last;

            public string LastSha { get; private set; } = lastSha;

            public void Include(DateTimeOffset first, DateTimeOffset last, string lastSha)
            {
                if (first < First)
                {
                    First = first;
                }

                // Ties (same second) are broken by SHA so that the result does not depend on the worker split.
                if (last > Last || (last == Last && string.CompareOrdinal(lastSha, LastSha) > 0))
                {
                    Last = last;
                    LastSha = lastSha;
                }
            }
        }
    }
}
