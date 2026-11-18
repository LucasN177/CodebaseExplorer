using CodebaseExplorer.Contracts.Graph;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Dependencies;
using CodebaseExplorer.Domain.Entities;
using CodebaseExplorer.Domain.Evidence;
using CodebaseExplorer.Domain.Files;
using CodebaseExplorer.Domain.Model;
using CodebaseExplorer.Domain.Projects;
using CodebaseExplorer.Domain.Relationships;

namespace CodebaseExplorer.Graph.Projection;

/// <summary>
/// Evaluates <see cref="GraphFilters"/> against a model (GRAPH-007): which projects, external dependencies and code
/// elements are shown and which relationships are aggregated into edges.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Test code: a project is a test project if it owns code files and all of them are classified <c>Test</c>;
/// an entity is test code if all its declarations lie in test files; a relationship is test-only if all its evidence
/// locations lie in test files. Generated code (<c>Generated</c> files) is handled the same way.</item>
/// <item>Languages: projects by primary language, entities by their language. Ecosystems: projects by ecosystem,
/// external dependencies by the ecosystem of their package manager.</item>
/// <item>Relationship kinds and the minimum evidence level only filter relationships.</item>
/// </list>
/// </remarks>
internal sealed class GraphFilter
{
    private readonly HashSet<ElementId> _excludedFiles;
    private readonly HashSet<string> _languages;
    private readonly HashSet<string> _ecosystems;
    private readonly HashSet<string> _kinds;
    private readonly EvidenceLevel _minLevel;
    private readonly HashSet<ElementId> _testProjects;
    private readonly Dictionary<ElementId, bool> _entityShown = [];
    private readonly ArchitectureModel _model;

    public GraphFilter(ArchitectureModel model, GraphFilters filters)
    {
        _model = model;
        _languages = filters.Languages.ToHashSet(StringComparer.Ordinal);
        _ecosystems = filters.Ecosystems.ToHashSet(StringComparer.Ordinal);
        _kinds = filters.RelationshipKinds.ToHashSet(StringComparer.Ordinal);
        _minLevel = filters.MinLevel switch
        {
            GraphEvidenceLevels.Confirmed => EvidenceLevel.Confirmed,
            GraphEvidenceLevels.Syntactic => EvidenceLevel.Syntactic,
            _ => EvidenceLevel.Heuristic,
        };

        _excludedFiles = model.Files
            .Where(f => (!filters.IncludeTests && f.Classification == FileClassification.Test)
                || (!filters.IncludeGenerated && f.Classification == FileClassification.Generated))
            .Select(f => f.Id)
            .ToHashSet();

        _testProjects = filters.IncludeTests
            ? []
            : model.Files
                .Where(f => f.ProjectId is not null && f.Classification is FileClassification.Source or FileClassification.Test)
                .GroupBy(f => f.ProjectId!.Value)
                .Where(g => g.All(f => f.Classification == FileClassification.Test))
                .Select(g => g.Key)
                .ToHashSet();
    }

    public bool Shows(CodeProject project) =>
        !_testProjects.Contains(project.Id)
        && (_languages.Count == 0 || _languages.Contains(project.PrimaryLanguage.Value))
        && (_ecosystems.Count == 0 || _ecosystems.Contains(project.Ecosystem.ToKeyToken()));

    public bool Shows(ExternalDependency dependency) =>
        _ecosystems.Count == 0 || _ecosystems.Contains(EcosystemOf(dependency.Ecosystem));

    /// <summary>An entity is shown if its language passes and it (or, for members, its type) is not test/generated code.</summary>
    public bool Shows(CodeEntity entity)
    {
        if (!_entityShown.TryGetValue(entity.Id, out var shown))
        {
            shown = (_languages.Count == 0 || _languages.Contains(entity.Language.Value))
                && !(entity.Declarations.Count > 0 && entity.Declarations.All(d => _excludedFiles.Contains(d.FileId)))
                && (entity.ContainingEntityId is not { } container || _model.FindEntity(container) is not { } type || Shows(type));
            _entityShown[entity.Id] = shown;
        }

        return shown;
    }

    public bool Shows(CodeRelationship relationship)
    {
        if (_kinds.Count > 0 && !_kinds.Contains(relationship.Kind.ToKeyToken()))
        {
            return false;
        }

        if (relationship.Level < _minLevel)
        {
            return false;
        }

        // Found only in test or generated files (every evidence has a location there)?
        return _excludedFiles.Count == 0
            || !relationship.Evidence.All(e => e.Location is { } location && _excludedFiles.Contains(location.FileId));
    }

    /// <summary>The graph ecosystem token of a package ecosystem.</summary>
    public static string EcosystemOf(PackageEcosystem ecosystem) => ecosystem switch
    {
        PackageEcosystem.NuGet or PackageEcosystem.DotNetFramework => "dotnet",
        PackageEcosystem.Npm => "node",
        PackageEcosystem.PyPI => "python",
        _ => "other",
    };
}
