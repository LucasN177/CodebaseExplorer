using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Entities;
using CodebaseExplorer.Domain.Model;
using CodebaseExplorer.Domain.Modules;
using CodebaseExplorer.Domain.Repositories;
using CodebaseExplorer.Domain.Runs;
using CodebaseExplorer.Storage.Persistence;
using CodebaseExplorer.Storage.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace CodebaseExplorer.Storage.Stores;

/// <summary>
/// SQLite implementation of <see cref="IArchitectureModelStore"/> (STORE-004, DATABASE_SCHEMA.md §2).
/// </summary>
/// <remarks>
/// <para>
/// Saving a model replaces the repository's current model in one transaction: delete by repository, then
/// insert in batches of <see cref="BatchSize"/> rows with a cleared change tracker, so memory stays bounded
/// by the batch, not by the model. The insert order follows the foreign keys (§2 "Insert order").
/// </para>
/// <para>
/// Reading returns the whole current model; the most recently loaded model is kept per run id, so repeated
/// reads (graph, edge details) do not hit the database while the model is current.
/// </para>
/// </remarks>
internal sealed class ArchitectureModelStore(IDbContextFactory<CodebaseExplorerDbContext> contextFactory) : IArchitectureModelStore, IDisposable
{
    public const int BatchSize = 5_000;

    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private StoredRepositoryModel? _cached;

    public async Task<Repository?> FindRepositoryAsync(RepositoryId repositoryId, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var record = await context.Repositories.AsNoTracking().SingleOrDefaultAsync(r => r.Id == repositoryId.Value, cancellationToken);
        return record is null ? null : RecordMapper.ToDomain(record);
    }

    public async Task<Repository?> FindRepositoryByRootAsync(string rootPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var record = await context.Repositories.AsNoTracking().SingleOrDefaultAsync(r => r.LocalPath == rootPath, cancellationToken);
        if (record is null && (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()))
        {
            // Case-insensitive file systems: the same folder may be selected with different casing.
            var candidates = await context.Repositories.AsNoTracking().ToListAsync(cancellationToken);
            record = candidates.FirstOrDefault(r => string.Equals(r.LocalPath, rootPath, StringComparison.OrdinalIgnoreCase));
        }

        return record is null ? null : RecordMapper.ToDomain(record);
    }

    public async Task SaveRunAsync(Repository repository, AnalysisRun run, ArchitectureModel? model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(run);

        if (run.RepositoryId != repository.Id || !run.IsTerminal)
        {
            throw new ArgumentException("Only a finished run of this repository can be saved.", nameof(run));
        }

        var replacesModel = run.ProducedModel;
        if (replacesModel)
        {
            if (model is null || model.RepositoryId != repository.Id || model.AnalysisRunId != run.Id)
            {
                throw new ArgumentException("A run that produced a model must be saved with that model.", nameof(model));
            }

            if (repository.LastAnalysisRunId != run.Id)
            {
                throw new ArgumentException("The repository must be marked as analyzed by the run before saving its model.", nameof(repository));
            }

            model.EnsureValid();
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        context.ChangeTracker.AutoDetectChangesEnabled = false;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        // Repository first (without its current run: that row may not exist yet), then the run.
        var repositoryRecord = await context.Repositories.SingleOrDefaultAsync(r => r.Id == repository.Id.Value, cancellationToken);
        if (repositoryRecord is null)
        {
            repositoryRecord = RecordMapper.ToRecord(repository);
            repositoryRecord.LastAnalysisRunId = null;
            context.Repositories.Add(repositoryRecord);
        }

        context.AnalysisRuns.Add(RecordMapper.ToRecord(run));
        await SaveAndClearAsync(context, cancellationToken);

        if (replacesModel)
        {
            await DeleteModelAsync(context, repository.Id.Value, cancellationToken);
            await InsertModelAsync(context, model!, cancellationToken);
            await context.Repositories
                .Where(r => r.Id == repository.Id.Value)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.LastAnalysisRunId, run.Id.Value), cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        await InvalidateAsync(repository.Id);
    }

    public async Task<StoredRepositoryModel?> LoadCurrentModelAsync(RepositoryId repositoryId, CancellationToken cancellationToken)
    {
        await _cacheLock.WaitAsync(cancellationToken);
        try
        {
            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
            var repositoryRecord = await context.Repositories.AsNoTracking().SingleOrDefaultAsync(r => r.Id == repositoryId.Value, cancellationToken);
            if (repositoryRecord?.LastAnalysisRunId is not { } runId)
            {
                return null;
            }

            if (_cached is { } cached && cached.Repository.Id == repositoryId && cached.Run.Id.Value == runId)
            {
                return cached;
            }

            _cached = await ReadModelAsync(context, repositoryRecord, runId, cancellationToken);
            return _cached;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    public void Dispose() => _cacheLock.Dispose();

    private async Task InvalidateAsync(RepositoryId repositoryId)
    {
        await _cacheLock.WaitAsync(CancellationToken.None);
        try
        {
            if (_cached?.Repository.Id == repositoryId)
            {
                _cached = null;
            }
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private static async Task DeleteModelAsync(CodebaseExplorerDbContext context, Guid repositoryId, CancellationToken cancellationToken)
    {
        // Children first; evidence and declarations go with their relationship/entity (cascade).
        await context.CodeRelationships.Where(r => r.RepositoryId == repositoryId).ExecuteDeleteAsync(cancellationToken);
        await context.AnalysisEvidence.Where(e => e.RepositoryId == repositoryId).ExecuteDeleteAsync(cancellationToken);
        await context.CodeEntities.Where(e => e.RepositoryId == repositoryId).ExecuteDeleteAsync(cancellationToken);
        await context.Modules.Where(m => m.RepositoryId == repositoryId).ExecuteDeleteAsync(cancellationToken);
        await context.ExternalDependencies.Where(d => d.RepositoryId == repositoryId).ExecuteDeleteAsync(cancellationToken);
        await context.Projects.Where(p => p.RepositoryId == repositoryId).ExecuteDeleteAsync(cancellationToken);
        await context.Files.Where(f => f.RepositoryId == repositoryId).ExecuteDeleteAsync(cancellationToken);
    }

    private static async Task InsertModelAsync(CodebaseExplorerDbContext context, ArchitectureModel model, CancellationToken cancellationToken)
    {
        var runId = model.AnalysisRunId;

        // Projects reference their manifest file and files reference their project: projects go in without
        // the manifest link, then files, then the links are set (DATABASE_SCHEMA.md §2 "Insert order").
        var projectRecords = model.Projects.Select(p => RecordMapper.ToRecord(p, runId)).ToList();
        var manifests = projectRecords.Where(p => p.ProjectFileId is not null).ToDictionary(p => p.Id, p => p.ProjectFileId!.Value);
        projectRecords.ForEach(p => p.ProjectFileId = null);
        await InsertBatchedAsync(context, projectRecords, cancellationToken);

        await InsertBatchedAsync(context, model.Files.Select(f => RecordMapper.ToRecord(f, runId)), cancellationToken);
        foreach (var (projectId, fileId) in manifests)
        {
            await context.Projects.Where(p => p.Id == projectId).ExecuteUpdateAsync(s => s.SetProperty(p => p.ProjectFileId, fileId), cancellationToken);
        }

        // Parents before children across batch boundaries.
        await InsertBatchedAsync(context, OrderByDepth(model.Modules, m => m.ParentModuleId, m => m.Id).Select(m => RecordMapper.ToRecord(m, runId)), cancellationToken);
        await InsertBatchedAsync(context, OrderByDepth(model.Entities, e => e.ContainingEntityId, e => e.Id).Select(e => RecordMapper.ToRecord(e, runId)), cancellationToken);
        await InsertBatchedAsync(context, model.ExternalDependencies.Select(d => RecordMapper.ToRecord(d, runId)), cancellationToken);
        await InsertBatchedAsync(context, model.Relationships.Select(r => RecordMapper.ToRecord(r, model, runId)), cancellationToken);
        await InsertBatchedAsync(context, model.Relationships.SelectMany(r => RecordMapper.ToEvidenceRecords(r, model.RepositoryId)), cancellationToken);
    }

    private static async Task InsertBatchedAsync<TRecord>(CodebaseExplorerDbContext context, IEnumerable<TRecord> records, CancellationToken cancellationToken)
        where TRecord : class
    {
        var batch = new List<TRecord>(BatchSize);
        foreach (var record in records)
        {
            batch.Add(record);
            if (batch.Count == BatchSize)
            {
                context.Set<TRecord>().AddRange(batch);
                await SaveAndClearAsync(context, cancellationToken);
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            context.Set<TRecord>().AddRange(batch);
            await SaveAndClearAsync(context, cancellationToken);
        }
    }

    private static async Task SaveAndClearAsync(CodebaseExplorerDbContext context, CancellationToken cancellationToken)
    {
        context.ChangeTracker.DetectChanges();
        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();
    }

    /// <summary>Orders a tree (parent pointer per element) so that every parent comes before its children.</summary>
    private static IEnumerable<T> OrderByDepth<T>(IReadOnlyCollection<T> elements, Func<T, ElementId?> parentOf, Func<T, ElementId> idOf)
    {
        var parents = elements.ToDictionary(idOf, parentOf);

        int Depth(ElementId id)
        {
            var depth = 0;
            for (var current = parents.GetValueOrDefault(id); current is { } parent; current = parents.GetValueOrDefault(parent))
            {
                depth++;
            }

            return depth;
        }

        return elements.OrderBy(e => Depth(idOf(e)));
    }

    private static async Task<StoredRepositoryModel> ReadModelAsync(
        CodebaseExplorerDbContext context, RepositoryRecord repositoryRecord, Guid runId, CancellationToken cancellationToken)
    {
        var repositoryId = repositoryRecord.Id;
        var run = RecordMapper.ToDomain(await context.AnalysisRuns.AsNoTracking().Include(r => r.Diagnostics)
            .SingleAsync(r => r.Id == runId, cancellationToken));

        var files = (await context.Files.AsNoTracking().Where(f => f.RepositoryId == repositoryId).ToListAsync(cancellationToken))
            .Select(RecordMapper.ToDomain).ToList();
        var filePaths = files.ToDictionary(f => f.Id.Value, f => f.Path.Value);
        var projects = (await context.Projects.AsNoTracking().Where(p => p.RepositoryId == repositoryId).ToListAsync(cancellationToken))
            .Select(p => RecordMapper.ToDomain(p, filePaths)).ToList();
        var projectsById = projects.ToDictionary(p => p.Id);
        IReadOnlyList<CodeModule> modules = RecordMapper.ToDomain(
            await context.Modules.AsNoTracking().Where(m => m.RepositoryId == repositoryId).ToListAsync(cancellationToken), projectsById);
        IReadOnlyList<CodeEntity> entities = RecordMapper.ToDomain(
            await context.CodeEntities.AsNoTracking().Include(e => e.Declarations).Where(e => e.RepositoryId == repositoryId).ToListAsync(cancellationToken),
            projectsById,
            modules.ToDictionary(m => m.Id));
        var dependencies = (await context.ExternalDependencies.AsNoTracking().Where(d => d.RepositoryId == repositoryId).ToListAsync(cancellationToken))
            .Select(RecordMapper.ToDomain).ToList();
        var evidence = (await context.AnalysisEvidence.AsNoTracking().Where(e => e.RepositoryId == repositoryId).ToListAsync(cancellationToken))
            .ToLookup(e => e.RelationshipId);
        var relationships = (await context.CodeRelationships.AsNoTracking().Where(r => r.RepositoryId == repositoryId).ToListAsync(cancellationToken))
            .Select(r => RecordMapper.ToDomain(r, evidence[r.Id])).ToList();

        var model = ArchitectureModel.Create(new RepositoryId(repositoryId), run.Id, files, projects, modules, entities, dependencies, relationships);
        model.EnsureValid();

        return new StoredRepositoryModel(RecordMapper.ToDomain(repositoryRecord), run, model);
    }
}
