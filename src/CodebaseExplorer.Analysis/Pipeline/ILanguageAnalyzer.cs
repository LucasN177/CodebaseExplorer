using CodebaseExplorer.Domain.Common;

namespace CodebaseExplorer.Analysis.Pipeline;

/// <summary>
/// Stage 3 analyzer for one or more languages: produces modules, entities and code-level
/// relationships for one project at a time (ANALYSIS_ENGINE.md §3). Independent of all other
/// analyzers; reads only through the context's file reader; never executes anything.
/// </summary>
internal interface ILanguageAnalyzer
{
    AnalyzerDescriptor Descriptor { get; }

    bool Supports(LanguageId language);

    Task<AnalyzerFragment> AnalyzeProjectAsync(LanguageAnalysisContext context, CancellationToken cancellationToken);
}
