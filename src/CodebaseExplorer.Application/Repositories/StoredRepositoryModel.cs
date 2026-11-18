using CodebaseExplorer.Domain.Model;
using CodebaseExplorer.Domain.Repositories;
using CodebaseExplorer.Domain.Runs;

namespace CodebaseExplorer.Application.Repositories;

/// <summary>A repository with the run that produced its current model, and that model.</summary>
/// <param name="Repository">The registered repository.</param>
/// <param name="Run">The run of the current model (succeeded or partially succeeded).</param>
/// <param name="Model">The validated current model.</param>
public sealed record StoredRepositoryModel(Repository Repository, AnalysisRun Run, ArchitectureModel Model);
