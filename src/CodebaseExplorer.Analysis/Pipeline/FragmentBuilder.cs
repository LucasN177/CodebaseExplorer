using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Dependencies;
using CodebaseExplorer.Domain.Entities;
using CodebaseExplorer.Domain.Evidence;
using CodebaseExplorer.Domain.Files;
using CodebaseExplorer.Domain.Modules;
using CodebaseExplorer.Domain.Projects;
using CodebaseExplorer.Domain.Relationships;
using CodebaseExplorer.Domain.Runs;

namespace CodebaseExplorer.Analysis.Pipeline;

/// <summary>
/// Collects the content of one <see cref="AnalyzerFragment"/> and creates evidence-backed relationships and
/// diagnostics attributed to the analyzer. Relationships with the same (source, kind, target) may be added several
/// times; the linker merges them into one relationship with all evidence (first attributes win).
/// </summary>
internal sealed class FragmentBuilder(RepositoryId repositoryId, AnalysisRunId runId, AnalyzerDescriptor descriptor)
{
    public List<CodeProject> Projects { get; } = [];

    public List<CodeModule> Modules { get; } = [];

    public List<CodeEntity> Entities { get; } = [];

    public List<ExternalDependency> ExternalDependencies { get; } = [];

    public List<CodeRelationship> Relationships { get; } = [];

    public List<AnalysisDiagnostic> Diagnostics { get; } = [];

    /// <summary>A location in a repository file (1-based line and column).</summary>
    public SourceLocation At(RelativePath file, int line, int column) =>
        SourceLocation.Line(RepositoryFile.IdFor(repositoryId, file), Math.Max(1, line), Math.Max(1, column));

    public void AddRelationship(
        ElementReference source,
        RelationshipKind kind,
        ElementReference target,
        EvidenceLevel level,
        string rule,
        SourceLocation location,
        IReadOnlyDictionary<string, string>? attributes = null)
    {
        var evidence = AnalysisEvidence.Create(
            CodeRelationship.IdFor(source, kind, target), descriptor.Id, descriptor.Version, level, rule, location, runId);
        Relationships.Add(CodeRelationship.Create(source, kind, target, [evidence], attributes));
    }

    public void Report(DiagnosticSeverity severity, string code, string message, SourceLocation? location = null) =>
        Diagnostics.Add(new AnalysisDiagnostic(severity, code, message, descriptor.Id, location));

    public AnalyzerFragment Build() => new()
    {
        Projects = Projects,
        Modules = Modules,
        Entities = Entities,
        ExternalDependencies = ExternalDependencies,
        Relationships = Relationships,
        Diagnostics = Diagnostics,
    };
}
