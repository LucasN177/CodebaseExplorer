using CodebaseExplorer.Application.AI;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Storage.Persistence;
using CodebaseExplorer.Storage.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace CodebaseExplorer.Storage.Stores;

/// <summary>SQLite implementation of <see cref="IAiArtifactStore"/> (AI-008, DATABASE_SCHEMA.md §4.16).</summary>
internal sealed class AiArtifactStore(IDbContextFactory<CodebaseExplorerDbContext> contextFactory) : IAiArtifactStore
{
    public async Task<AiArtifact?> FindAsync(RepositoryId repositoryId, ElementId elementId, string artifactKind, AnalysisRunId runId,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var record = await context.AiArtifacts.AsNoTracking()
            .Where(a => a.RepositoryId == repositoryId.Value && a.ElementId == elementId.Value && a.ArtifactKind == artifactKind
                && a.AnalysisRunId == runId.Value)
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return record is null
            ? null
            : new AiArtifact(record.Id, new RepositoryId(record.RepositoryId), record.MessageId,
                Enum.TryParse<ElementKind>(record.ElementKind, out var kind) ? kind : ElementKind.Entity,
                new ElementId(record.ElementId), record.ArtifactKind, record.Content, new AnalysisRunId(record.AnalysisRunId), record.CreatedAt);
    }

    public async Task SaveAsync(AiArtifact artifact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.AiArtifacts
            .Where(a => a.RepositoryId == artifact.RepositoryId.Value && a.ElementId == artifact.ElementId.Value && a.ArtifactKind == artifact.ArtifactKind)
            .ExecuteDeleteAsync(cancellationToken);
        context.AiArtifacts.Add(new AiArtifactRecord
        {
            Id = artifact.Id,
            RepositoryId = artifact.RepositoryId.Value,
            MessageId = artifact.MessageId,
            ElementKind = artifact.ElementKind.ToString(),
            ElementId = artifact.ElementId.Value,
            ArtifactKind = artifact.ArtifactKind,
            Content = artifact.Content,
            AnalysisRunId = artifact.AnalysisRunId.Value,
            CreatedAt = artifact.CreatedAt,
        });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
