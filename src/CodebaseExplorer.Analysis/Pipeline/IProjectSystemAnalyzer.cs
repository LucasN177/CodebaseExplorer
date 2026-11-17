using CodebaseExplorer.Analysis.Scanning;

namespace CodebaseExplorer.Analysis.Pipeline;

/// <summary>
/// Stage 2 analyzer for one ecosystem (.NET, Node, Python): reads manifests and produces projects,
/// external dependencies and project-level relationships (ANALYSIS_ENGINE.md §3). Independent of all
/// other analyzers; reads only through the context's file reader; never executes anything.
/// </summary>
internal interface IProjectSystemAnalyzer
{
    AnalyzerDescriptor Descriptor { get; }

    /// <summary>True if the scan contains files this analyzer handles (e.g. a project marker).</summary>
    bool AppliesTo(ScanResult scan);

    Task<AnalyzerFragment> AnalyzeAsync(ProjectSystemContext context, CancellationToken cancellationToken);
}
