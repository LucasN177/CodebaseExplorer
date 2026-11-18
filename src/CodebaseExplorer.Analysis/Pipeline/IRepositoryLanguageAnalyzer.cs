using CodebaseExplorer.Domain.Common;

namespace CodebaseExplorer.Analysis.Pipeline;

/// <summary>
/// Stage 3 analyzer that needs all projects of its language at once, because a project's results
/// depend on the projects it references — e.g. C#: a project is compiled against the compilations of
/// its referenced in-repository projects in topological order (ANALYSIS_ENGINE.md §3, ADR-004).
/// Independent of all other analyzers; reads only through the context's file reader; never executes
/// anything. Failures of single projects are isolated by the analyzer and reported as diagnostics.
/// </summary>
internal interface IRepositoryLanguageAnalyzer
{
    AnalyzerDescriptor Descriptor { get; }

    bool Supports(LanguageId language);

    Task<AnalyzerFragment> AnalyzeAsync(RepositoryLanguageContext context, CancellationToken cancellationToken);
}
