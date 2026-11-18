using CodebaseExplorer.Analysis.Scanning;
using CodebaseExplorer.Application.Analysis;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Projects;

namespace CodebaseExplorer.Analysis.Pipeline;

/// <summary>Read-only input of a language analyzer for one project.</summary>
/// <param name="RepositoryId">Repository being analyzed.</param>
/// <param name="AnalysisRunId">Run that evidence is attributed to.</param>
/// <param name="Root">Validated repository root.</param>
/// <param name="Project">The project to analyze.</param>
/// <param name="ProjectFiles">The files of <paramref name="Project"/> in languages the analyzer supports.</param>
/// <param name="AllFiles">All scanned files (for resolution hints only).</param>
/// <param name="Limits">Size and parallelism limits.</param>
/// <param name="FileReader">The only permitted way to read repository content.</param>
/// <param name="Projects">All real (non-placeholder) projects from stage 2, ordered by natural key — for resolving
/// references into other projects (e.g. imports of workspace packages); never analyzed by this call.</param>
internal sealed record LanguageAnalysisContext(
    RepositoryId RepositoryId,
    AnalysisRunId AnalysisRunId,
    RepositoryRoot Root,
    CodeProject Project,
    IReadOnlyList<ScannedFile> ProjectFiles,
    IReadOnlyList<ScannedFile> AllFiles,
    AnalysisLimits Limits,
    IRepositoryFileReader FileReader,
    IReadOnlyList<CodeProject> Projects);
