using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Runs;

namespace CodebaseExplorer.Analysis.ProjectSystems.DotNet;

/// <summary>Result of parsing one solution file; parsing never throws on malformed input.</summary>
/// <param name="SolutionPath">Repository-relative path of the solution file.</param>
/// <param name="Projects">Projects in file order.</param>
/// <param name="Diagnostics">Problems found while parsing.</param>
internal sealed record SolutionDocument(
    RelativePath SolutionPath,
    IReadOnlyList<SolutionProjectEntry> Projects,
    IReadOnlyList<AnalysisDiagnostic> Diagnostics);
