using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Model;
using CodebaseExplorer.Domain.Repositories;
using CodebaseExplorer.Domain.Runs;

namespace CodebaseExplorer.Application.Repositories;

/// <summary>
/// Persistent store of repositories, analysis runs and the current model of each repository
/// (STORE-004, DATABASE_SCHEMA.md §2). Replaces the M1 session cache; models survive restarts.
/// </summary>
/// <remarks>
/// M2 reads the whole current model (cached per run); slice queries for large repositories follow with
/// the node budget and benchmarks in M4 (GRAPH-008, QA-002).
/// </remarks>
public interface IArchitectureModelStore
{
    Task<Repository?> FindRepositoryAsync(RepositoryId repositoryId, CancellationToken cancellationToken);

    /// <summary>Finds the repository registered for an analyzed folder (path as returned by root validation).</summary>
    Task<Repository?> FindRepositoryByRootAsync(string rootPath, CancellationToken cancellationToken);

    /// <summary>All registered repositories with their last opening and most recent run (REPO-005), in no particular order.</summary>
    Task<IReadOnlyList<RepositoryListEntry>> ListRepositoriesAsync(CancellationToken cancellationToken);

    /// <summary>Records that the user opened (or imported) the repository; unknown ids are ignored.</summary>
    Task MarkOpenedAsync(RepositoryId repositoryId, DateTimeOffset openedAt, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the repository with all its runs, diagnostics and model data (cascade, REPO-005).
    /// Returns <see langword="false"/> if the repository is unknown.
    /// </summary>
    Task<bool> DeleteRepositoryAsync(RepositoryId repositoryId, CancellationToken cancellationToken);

    /// <summary>
    /// Records a finished run. The repository is registered if it is new; the run and its diagnostics are
    /// always stored. Only a run that produced a model (<see cref="AnalysisRun.ProducedModel"/>) replaces the
    /// repository's current model — in one transaction, so a failure leaves the previous model intact.
    /// Failed and cancelled runs never change the stored model.
    /// </summary>
    /// <param name="repository">The repository; its <see cref="Repository.LastAnalysisRunId"/> must point to <paramref name="run"/> when a model is saved.</param>
    /// <param name="run">A terminal run of that repository.</param>
    /// <param name="model">The run's model; required when the run produced one, ignored otherwise.</param>
    /// <param name="cancellationToken">Cancels the operation; the transaction is rolled back.</param>
    Task SaveRunAsync(Repository repository, AnalysisRun run, ArchitectureModel? model, CancellationToken cancellationToken);

    /// <summary>The repository's current model, or <see langword="null"/> if the repository is unknown or has no model yet.</summary>
    Task<StoredRepositoryModel?> LoadCurrentModelAsync(RepositoryId repositoryId, CancellationToken cancellationToken);
}
