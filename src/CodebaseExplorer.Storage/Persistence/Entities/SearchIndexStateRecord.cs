namespace CodebaseExplorer.Storage.Persistence.Entities;

/// <summary>Table <c>SearchIndexStates</c> (§4.18): which run the repository's FTS5 chunks belong to.</summary>
internal sealed class SearchIndexStateRecord
{
    public Guid RepositoryId { get; set; }

    public Guid AnalysisRunId { get; set; }

    public int ChunkCount { get; set; }

    public DateTimeOffset IndexedAt { get; set; }
}
