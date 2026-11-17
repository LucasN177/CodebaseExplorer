using CodebaseExplorer.Domain.Dependencies;
using CodebaseExplorer.Domain.Entities;
using CodebaseExplorer.Domain.Modules;
using CodebaseExplorer.Domain.Projects;
using CodebaseExplorer.Domain.Relationships;
using CodebaseExplorer.Domain.Runs;

namespace CodebaseExplorer.Analysis.Pipeline;

/// <summary>
/// What one analyzer produced. Elements are Domain objects whose ids derive from natural keys, so
/// fragments of different analyzers merge by id (= by natural key) in the linker. Files are not part
/// of fragments; they come from the scan.
/// </summary>
internal sealed record AnalyzerFragment
{
    public static AnalyzerFragment Empty { get; } = new();

    public IReadOnlyList<CodeProject> Projects { get; init; } = [];

    public IReadOnlyList<CodeModule> Modules { get; init; } = [];

    public IReadOnlyList<CodeEntity> Entities { get; init; } = [];

    public IReadOnlyList<ExternalDependency> ExternalDependencies { get; init; } = [];

    /// <summary>Each relationship carries at least one evidence record (enforced by the Domain).</summary>
    public IReadOnlyList<CodeRelationship> Relationships { get; init; } = [];

    public IReadOnlyList<AnalysisDiagnostic> Diagnostics { get; init; } = [];
}
