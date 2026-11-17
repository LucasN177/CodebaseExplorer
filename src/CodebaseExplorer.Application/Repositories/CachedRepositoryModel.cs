using CodebaseExplorer.Domain.Model;
using CodebaseExplorer.Domain.Repositories;
using CodebaseExplorer.Domain.Runs;

namespace CodebaseExplorer.Application.Repositories;

/// <summary>A repository with the run that produced its current model.</summary>
/// <param name="Repository">The registered repository.</param>
/// <param name="Run">The completed run (succeeded or partially succeeded).</param>
/// <param name="Model">The validated model of that run.</param>
public sealed record CachedRepositoryModel(Repository Repository, AnalysisRun Run, ArchitectureModel Model);
