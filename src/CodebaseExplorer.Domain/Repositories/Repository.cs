using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Runs;

namespace CodebaseExplorer.Domain.Repositories;

/// <summary>
/// A repository registered by the user; aggregate root for its analysis runs and model
/// (DOMAIN_MODEL.md §4.1).
/// </summary>
public sealed class Repository
{
    /// <summary>Restores a repository with a known identity (e.g. from storage).</summary>
    public Repository(
        RepositoryId id,
        string name,
        RepositoryLocation location,
        DateTimeOffset addedAt,
        AnalysisRunId? lastAnalysisRunId = null)
    {
        if (id.Value == Guid.Empty)
        {
            throw new ArgumentException("A repository requires an id.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(location);

        Id = id;
        Name = name.Trim();
        Location = location;
        AddedAt = addedAt;
        LastAnalysisRunId = lastAnalysisRunId;
    }

    public RepositoryId Id { get; }

    public string Name { get; }

    public RepositoryLocation Location { get; }

    public DateTimeOffset AddedAt { get; }

    /// <summary>The run whose model is current; <see langword="null"/> until the first successful run.</summary>
    public AnalysisRunId? LastAnalysisRunId { get; private set; }

    /// <summary>Makes the model of <paramref name="run"/> the current one.</summary>
    /// <exception cref="ArgumentException">The run belongs to another repository or produced no model.</exception>
    public void MarkAnalyzed(AnalysisRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        if (run.RepositoryId != Id)
        {
            throw new ArgumentException("The run belongs to another repository.", nameof(run));
        }

        if (!run.ProducedModel)
        {
            throw new ArgumentException($"A {run.Status} run has no model to make current.", nameof(run));
        }

        LastAnalysisRunId = run.Id;
    }

    /// <summary>Registers a new repository with a fresh time-ordered id.</summary>
    public static Repository Register(string name, RepositoryLocation location, DateTimeOffset addedAt) =>
        new(RepositoryId.New(addedAt), name, location, addedAt);
}
