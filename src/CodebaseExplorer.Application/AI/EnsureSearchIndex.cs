using System.Security.Cryptography;
using CodebaseExplorer.Application.Analysis;
using CodebaseExplorer.Application.Common;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Domain.Common;

namespace CodebaseExplorer.Application.AI;

/// <summary>
/// Builds the search index of a repository's current run on first use (AI-003, ADR-013): every file the
/// <see cref="SecretFilePolicy"/> allows is read through <see cref="IRepositoryFileReader"/> and chunked. Files that
/// changed since the analysis are skipped, so cited line numbers always match the analyzed version. Nothing is indexed
/// while AI is unused; the index is rebuilt when the repository has a new run.
/// </summary>
public sealed class EnsureSearchIndex(
    ISearchIndexStore index,
    IRepositoryRootValidator rootValidator,
    IRepositoryFileReader fileReader,
    TimeProvider time)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Returns the state of an up-to-date index, building it if needed.</summary>
    public async Task<Result<SearchIndexState>> ExecuteAsync(StoredRepositoryModel stored, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stored);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var repositoryId = stored.Repository.Id;
            if (await index.GetStateAsync(repositoryId, cancellationToken) is { } state && state.RunId == stored.Model.AnalysisRunId)
            {
                return state;
            }

            if (!rootValidator.Validate(stored.Repository.Location.LocalPath).TryGetValue(out var root, out var rootError))
            {
                return ApplicationError.NotFound(Source.SourceErrorCodes.FolderMissing,
                    $"The repository folder is not available any more ({rootError.Message}). Re-analyze or remove the repository.");
            }

            var chunks = new List<SearchChunk>();
            foreach (var file in stored.Model.Files.Where(SecretFilePolicy.IsAllowed))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bytes = await fileReader.ReadAllBytesAsync(root, file.Path, AnalysisLimits.Default.MaxParseFileBytes, cancellationToken);
                if (!bytes.TryGetValue(out var content, out _)
                    || ContentHash.FromBytes(SHA256.HashData(content)) != file.Hash
                    || content.AsSpan(0, Math.Min(content.Length, 8000)).Contains((byte)0))
                {
                    continue;
                }

                var text = await fileReader.ReadAllTextAsync(root, file.Path, AnalysisLimits.Default.MaxParseFileBytes, cancellationToken);
                if (!text.TryGetValue(out var source, out _))
                {
                    continue;
                }

                chunks.AddRange(SourceChunker.Chunk(file.Id, source));
            }

            var indexedAt = time.GetUtcNow();
            await index.ReplaceAsync(repositoryId, stored.Model.AnalysisRunId, chunks, indexedAt, cancellationToken);
            return new SearchIndexState(stored.Model.AnalysisRunId, chunks.Count, indexedAt);
        }
        finally
        {
            _gate.Release();
        }
    }
}
