using CodebaseExplorer.Domain.Common;

namespace CodebaseExplorer.Application.AI;

/// <summary>
/// Lexical search index over source chunks for AI retrieval (AI-003, DATABASE_SCHEMA.md §4.18: SQLite FTS5). The index
/// belongs to one analysis run of a repository; <see cref="EnsureSearchIndex"/> rebuilds it when the run changes.
/// </summary>
public interface ISearchIndexStore
{
    /// <summary>The run the repository's index was built for, or null if it has none.</summary>
    Task<SearchIndexState?> GetStateAsync(RepositoryId repositoryId, CancellationToken cancellationToken);

    /// <summary>Replaces the repository's chunks and state in one transaction.</summary>
    Task ReplaceAsync(RepositoryId repositoryId, AnalysisRunId runId, IReadOnlyList<SearchChunk> chunks, DateTimeOffset indexedAt,
        CancellationToken cancellationToken);

    /// <summary>
    /// The best <paramref name="limit"/> chunks matching any of the terms (BM25 ranking, best first). Terms are plain
    /// words from <see cref="SearchTerms"/>; the store quotes them, so no query syntax reaches FTS5.
    /// </summary>
    Task<IReadOnlyList<SearchHit>> SearchAsync(RepositoryId repositoryId, IReadOnlyList<string> terms, int limit,
        CancellationToken cancellationToken);
}

/// <summary>An indexed run.</summary>
public sealed record SearchIndexState(AnalysisRunId RunId, int ChunkCount, DateTimeOffset IndexedAt);

/// <summary>A range of lines of one file, with the identifiers in it split into words (<c>InvoiceService</c> → invoice, service).</summary>
public sealed record SearchChunk(ElementId FileId, int StartLine, int EndLine, string Text, string Keywords);

/// <summary>A search result; lower <see cref="Rank"/> is better (BM25 as reported by SQLite).</summary>
public sealed record SearchHit(ElementId FileId, int StartLine, int EndLine, string Text, double Rank);
