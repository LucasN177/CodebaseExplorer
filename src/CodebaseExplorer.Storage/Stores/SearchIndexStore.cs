using System.Data.Common;
using CodebaseExplorer.Application.AI;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Storage.Persistence;
using CodebaseExplorer.Storage.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CodebaseExplorer.Storage.Stores;

/// <summary>
/// SQLite FTS5 implementation of <see cref="ISearchIndexStore"/> (AI-003, DATABASE_SCHEMA.md §4.18). Chunks live in the
/// virtual table <c>SearchChunks</c> (ids as lowercase hex of the BLOB value, <c>rowid</c> = chunk id); the run they
/// belong to in <c>SearchIndexStates</c>. Deleting a repository removes both (trigger and cascade).
/// </summary>
internal sealed class SearchIndexStore(IDbContextFactory<CodebaseExplorerDbContext> contextFactory) : ISearchIndexStore
{
    public async Task<SearchIndexState?> GetStateAsync(RepositoryId repositoryId, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var record = await context.SearchIndexStates.AsNoTracking()
            .SingleOrDefaultAsync(s => s.RepositoryId == repositoryId.Value, cancellationToken);
        return record is null ? null : new SearchIndexState(new AnalysisRunId(record.AnalysisRunId), record.ChunkCount, record.IndexedAt);
    }

    public async Task ReplaceAsync(RepositoryId repositoryId, AnalysisRunId runId, IReadOnlyList<SearchChunk> chunks, DateTimeOffset indexedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var connection = context.Database.GetDbConnection();
        var repository = Hex(repositoryId.Value);

        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction.GetDbTransaction();
            delete.CommandText = "DELETE FROM SearchChunks WHERE RepositoryId = $repository;";
            Add(delete, "$repository", repository);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction.GetDbTransaction();
            insert.CommandText = "INSERT INTO SearchChunks (RepositoryId, FileId, StartLine, EndLine, Text, Keywords) " +
                "VALUES ($repository, $file, $start, $end, $text, $keywords);";
            var parameters = new[]
            {
                Add(insert, "$repository", repository), Add(insert, "$file", string.Empty), Add(insert, "$start", 0),
                Add(insert, "$end", 0), Add(insert, "$text", string.Empty), Add(insert, "$keywords", string.Empty),
            };
            await insert.PrepareAsync(cancellationToken);
            foreach (var chunk in chunks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                parameters[1].Value = Hex(chunk.FileId.Value);
                parameters[2].Value = chunk.StartLine;
                parameters[3].Value = chunk.EndLine;
                parameters[4].Value = chunk.Text;
                parameters[5].Value = chunk.Keywords;
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await context.SearchIndexStates.Where(s => s.RepositoryId == repositoryId.Value).ExecuteDeleteAsync(cancellationToken);
        context.SearchIndexStates.Add(new SearchIndexStateRecord
        {
            RepositoryId = repositoryId.Value,
            AnalysisRunId = runId.Value,
            ChunkCount = chunks.Count,
            IndexedAt = indexedAt,
        });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(RepositoryId repositoryId, IReadOnlyList<string> terms, int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        var match = MatchExpression(terms);
        if (match.Length == 0)
        {
            return [];
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await context.Database.OpenConnectionAsync(cancellationToken);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT FileId, StartLine, EndLine, Text, bm25(SearchChunks) AS Rank FROM SearchChunks " +
            "WHERE SearchChunks MATCH $match AND RepositoryId = $repository ORDER BY Rank, rowid LIMIT $limit;";
        Add(command, "$match", match);
        Add(command, "$repository", Hex(repositoryId.Value));
        Add(command, "$limit", limit);

        var hits = new List<SearchHit>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            hits.Add(new SearchHit(
                new ElementId(new Guid(Convert.FromHexString(reader.GetString(0)))),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.GetDouble(4)));
        }

        return hits;
    }

    /// <summary>
    /// <c>"term"* OR …</c>: every term quoted (FTS5 string), prefix match for terms of 4+ characters. Terms come from
    /// <see cref="SearchTerms"/> (letters and digits only); anything else is dropped, so no operator can be injected.
    /// </summary>
    internal static string MatchExpression(IReadOnlyList<string> terms) =>
        string.Join(" OR ", terms
            .Where(t => t.Length > 0 && t.All(char.IsAsciiLetterOrDigit))
            .Select(t => t.Length >= 4 ? $"\"{t}\"*" : $"\"{t}\""));

    private static string Hex(Guid value) => Convert.ToHexStringLower(value.ToByteArray());

    private static DbParameter Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
        return parameter;
    }
}
