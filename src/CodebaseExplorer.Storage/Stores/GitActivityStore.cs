using System.Text.Json;
using CodebaseExplorer.Application.Git;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Storage.Persistence;
using CodebaseExplorer.Storage.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace CodebaseExplorer.Storage.Stores;

/// <summary>
/// SQLite implementation of <see cref="IGitActivityStore"/> (GIT-005, DATABASE_SCHEMA.md §4.12–4.13). Statistics are
/// keyed by <c>Files.Id</c>: replacing a model deletes the old files and with them (cascade) the old statistics, so
/// only the current run's numbers survive for a file.
/// </summary>
internal sealed class GitActivityStore(IDbContextFactory<CodebaseExplorerDbContext> contextFactory) : IGitActivityStore
{
    public async Task SaveAsync(
        RepositoryId repositoryId,
        AnalysisRunId runId,
        GitSnapshotInfo snapshot,
        GitFileActivityReport activity,
        DateTimeOffset capturedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(activity);
        if (snapshot.HeadCommitSha is null)
        {
            return;
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        context.ChangeTracker.AutoDetectChangesEnabled = false;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var fileIds = await context.Files
            .Where(f => f.RepositoryId == repositoryId.Value)
            .Select(f => new { f.Id, f.RelativePath })
            .ToDictionaryAsync(f => f.RelativePath, f => f.Id, StringComparer.Ordinal, cancellationToken);

        var snapshotId = Guid.CreateVersion7(capturedAt);
        context.GitSnapshots.Add(new GitSnapshotRecord
        {
            Id = snapshotId,
            RepositoryId = repositoryId.Value,
            AnalysisRunId = runId.Value,
            HeadCommitSha = snapshot.HeadCommitSha,
            Branch = snapshot.Branch,
            IsDetachedHead = snapshot.IsDetachedHead,
            HasUncommittedChanges = snapshot.HasUncommittedChanges,
            RemotesJson = JsonSerializer.Serialize(
                snapshot.Remotes.Select(r => new GitRemoteJson(r.Name, GitRemoteUrls.Sanitize(r.Url))).ToList(),
                StorageJsonContext.Default.ListGitRemoteJson),
            HistoryDepth = activity.CommitsScanned,
            HistoryWindowStart = activity.OldestCommitAt,
            CapturedAt = capturedAt,
        });

        foreach (var file in activity.Files)
        {
            if (fileIds.TryGetValue(file.Path, out var fileId))
            {
                context.GitChanges.Add(new GitChangeRecord
                {
                    GitSnapshotId = snapshotId,
                    FileId = fileId,
                    CommitCount = file.CommitCount,
                    DistinctAuthorCount = file.DistinctAuthorCount,
                    LinesAdded = file.LinesAdded,
                    LinesDeleted = file.LinesDeleted,
                    FirstCommitAt = file.FirstCommitAt,
                    LastCommitAt = file.LastCommitAt,
                    LastCommitSha = file.LastCommitSha,
                });
            }
        }

        context.ChangeTracker.DetectChanges();
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<GitActivity?> LoadCurrentAsync(RepositoryId repositoryId, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var runId = await context.Repositories
            .Where(r => r.Id == repositoryId.Value)
            .Select(r => r.LastAnalysisRunId)
            .SingleOrDefaultAsync(cancellationToken);
        if (runId is null)
        {
            return null;
        }

        var snapshot = await context.GitSnapshots.AsNoTracking().SingleOrDefaultAsync(s => s.AnalysisRunId == runId.Value, cancellationToken);
        if (snapshot is null)
        {
            return null;
        }

        var files = await context.GitChanges.AsNoTracking()
            .Where(c => c.GitSnapshotId == snapshot.Id)
            .ToDictionaryAsync(
                c => new ElementId(c.FileId),
                c => new GitFileChanges(c.CommitCount, c.DistinctAuthorCount, c.LinesAdded ?? 0, c.LinesDeleted ?? 0,
                    c.FirstCommitAt, c.LastCommitAt, c.LastCommitSha),
                cancellationToken);

        var remotes = JsonSerializer.Deserialize(snapshot.RemotesJson, StorageJsonContext.Default.ListGitRemoteJson) ?? [];
        return new GitActivity(
            new GitActivitySnapshot(
                snapshot.HeadCommitSha,
                snapshot.Branch,
                snapshot.IsDetachedHead,
                snapshot.HasUncommittedChanges,
                [.. remotes.Select(r => new GitRemoteInfo(r.Name, r.Url))],
                snapshot.HistoryDepth,
                snapshot.HistoryWindowStart,
                snapshot.CapturedAt),
            files);
    }
}
