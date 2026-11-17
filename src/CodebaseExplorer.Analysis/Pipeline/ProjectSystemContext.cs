using CodebaseExplorer.Analysis.Scanning;
using CodebaseExplorer.Application.Analysis;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Domain.Common;

namespace CodebaseExplorer.Analysis.Pipeline;

/// <summary>Read-only input of a project-system analyzer.</summary>
internal sealed record ProjectSystemContext(
    RepositoryId RepositoryId,
    AnalysisRunId AnalysisRunId,
    RepositoryRoot Root,
    IReadOnlyList<ScannedFile> Files,
    AnalysisLimits Limits,
    IRepositoryFileReader FileReader);
