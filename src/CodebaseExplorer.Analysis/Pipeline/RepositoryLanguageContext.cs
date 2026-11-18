using CodebaseExplorer.Analysis.Scanning;
using CodebaseExplorer.Application.Analysis;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Projects;

namespace CodebaseExplorer.Analysis.Pipeline;

/// <summary>A project with its files in the languages an analyzer supports.</summary>
/// <param name="Project">A real (non-placeholder) project.</param>
/// <param name="Files">Files owned by the project (innermost project root) in supported languages.</param>
internal sealed record ProjectInput(CodeProject Project, IReadOnlyList<ScannedFile> Files);

/// <summary>Read-only input of a <see cref="IRepositoryLanguageAnalyzer"/>.</summary>
/// <param name="RepositoryId">Repository being analyzed.</param>
/// <param name="AnalysisRunId">Run that evidence is attributed to.</param>
/// <param name="Root">Validated repository root.</param>
/// <param name="Projects">Projects that own at least one supported file, ordered by natural key.</param>
/// <param name="ProjectReferences">Project → referenced project ids among all real projects (from stage 2).</param>
/// <param name="Limits">Size and parallelism limits.</param>
/// <param name="FileReader">The only permitted way to read repository content.</param>
/// <param name="ProjectCompleted">Called after each project with its name (progress reporting).</param>
internal sealed record RepositoryLanguageContext(
    RepositoryId RepositoryId,
    AnalysisRunId AnalysisRunId,
    RepositoryRoot Root,
    IReadOnlyList<ProjectInput> Projects,
    IReadOnlyDictionary<ElementId, IReadOnlyList<ElementId>> ProjectReferences,
    AnalysisLimits Limits,
    IRepositoryFileReader FileReader,
    Action<string> ProjectCompleted);
