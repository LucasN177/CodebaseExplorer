using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Dependencies;
using CodebaseExplorer.Domain.Entities;
using CodebaseExplorer.Domain.Evidence;
using CodebaseExplorer.Domain.Files;
using CodebaseExplorer.Domain.Model;
using CodebaseExplorer.Domain.Modules;
using CodebaseExplorer.Domain.Projects;
using CodebaseExplorer.Domain.Relationships;

namespace CodebaseExplorer.Graph.Tests;

/// <summary>Builds small, consistent architecture models by hand for projection tests.</summary>
internal sealed class ModelBuilder
{
    private static readonly ContentHash Hash =
        ContentHash.Parse("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");

    private readonly List<RepositoryFile> _files = [];
    private readonly List<CodeProject> _projects = [];
    private readonly List<CodeModule> _modules = [];
    private readonly List<CodeEntity> _entities = [];
    private readonly List<ExternalDependency> _dependencies = [];
    private readonly List<CodeRelationship> _relationships = [];

    public RepositoryId Repository { get; } = new(Guid.Parse("0193a1b2-c3d4-7e5f-8071-8293a4b5c6d7"));

    public AnalysisRunId Run { get; } = new(Guid.Parse("0193a1b2-c3d4-7e5f-8071-000000000007"));

    public CodeProject Project(string name, params string[] sourceFiles)
    {
        var manifest = File($"src/{name}/{name}.csproj", FileClassification.ProjectManifest);
        var project = CodeProject.Create(Repository, name, ProjectKind.DotNetProject, Ecosystem.DotNet, LanguageId.CSharp,
            manifest.Path, RelativePath.Create($"src/{name}"));
        _projects.Add(project);

        _files.Remove(manifest);
        _files.Add(File(manifest.Path.Value, FileClassification.ProjectManifest, project.Id));
        foreach (var source in sourceFiles)
        {
            _files.Add(File($"src/{name}/{source}", FileClassification.Source, project.Id));
        }

        return project;
    }

    /// <summary>A project of any ecosystem/language with files of given classifications (GRAPH-007 filter tests).</summary>
    public CodeProject Project(string name, Ecosystem ecosystem, LanguageId language, params (string Path, FileClassification Classification)[] files)
    {
        var manifest = RelativePath.Create($"src/{name}/manifest");
        var project = CodeProject.Create(Repository, name, ProjectKind.Unknown, ecosystem, language, manifest, RelativePath.Create($"src/{name}"));
        _projects.Add(project);
        _files.Add(File(manifest.Value, FileClassification.ProjectManifest, project.Id));
        foreach (var (path, classification) in files)
        {
            _files.Add(File($"src/{name}/{path}", classification, project.Id));
        }

        return project;
    }

    /// <summary>A type declared in a given file of its project (path relative to the project folder).</summary>
    public CodeEntity TypeIn(CodeProject project, CodeModule module, string name, string file, LanguageId? language = null)
    {
        var declaration = _files.First(f => f.Path.Value == $"{project.RootDirectory.Value}/{file}");
        var entity = CodeEntity.Create(project, module, EntityKind.Class, name, $"{module.QualifiedName}.{name}", language ?? project.PrimaryLanguage,
            $"T:{module.QualifiedName}.{name}", [SourceLocation.Line(declaration.Id, 1, 1, 10)]);
        _entities.Add(entity);
        return entity;
    }

    /// <summary>A relationship whose single evidence lies in a given repository file.</summary>
    public CodeRelationship RelateIn(
        string evidencePath, ElementKind sourceKind, ElementId source, RelationshipKind kind, ElementKind targetKind, ElementId target,
        EvidenceLevel level = EvidenceLevel.Confirmed)
    {
        var sourceRef = new ElementReference(sourceKind, source);
        var targetRef = new ElementReference(targetKind, target);
        var file = _files.First(f => f.Path.Value == evidencePath);
        var relationship = CodeRelationship.Create(sourceRef, kind, targetRef,
            [AnalysisEvidence.Create(CodeRelationship.IdFor(sourceRef, kind, targetRef), AnalyzerId.Create("test.analyzer"), "1", level,
                "test.rule", SourceLocation.Line(file.Id, 1, 1, 5), Run)]);
        _relationships.Add(relationship);
        return relationship;
    }

    public ExternalDependency Package(string name, PackageEcosystem ecosystem)
    {
        var dependency = ExternalDependency.Create(Repository, ecosystem, name);
        _dependencies.Add(dependency);
        return dependency;
    }

    public CodeProject Placeholder(string name)
    {
        var project = CodeProject.Create(Repository, name, ProjectKind.Unknown, Ecosystem.DotNet, LanguageId.CSharp, null,
            RelativePath.Create($"src/{name}"),
            new Dictionary<string, string>
            {
                [ProjectPropertyKeys.Resolution] = ProjectPropertyKeys.ResolutionMissing,
                [ProjectPropertyKeys.ReferencedPath] = $"src/{name}/{name}.csproj",
            });
        _projects.Add(project);
        return project;
    }

    public CodeModule Module(CodeProject project, string qualifiedName, CodeModule? parent = null)
    {
        var module = CodeModule.Create(project, ModuleKind.Namespace, qualifiedName.Split('.')[^1], qualifiedName, parent);
        _modules.Add(module);
        return module;
    }

    public CodeEntity Type(CodeProject project, CodeModule module, string name)
    {
        var file = _files.First(f => f.ProjectId == project.Id);
        var entity = CodeEntity.Create(project, module, EntityKind.Class, name, $"{module.QualifiedName}.{name}", LanguageId.CSharp,
            $"T:{module.QualifiedName}.{name}", [SourceLocation.Line(file.Id, 1, 1, 10)]);
        _entities.Add(entity);
        return entity;
    }

    public CodeEntity Member(CodeProject project, CodeModule module, CodeEntity type, string name)
    {
        var file = _files.First(f => f.ProjectId == project.Id);
        var entity = CodeEntity.Create(project, module, EntityKind.Method, name, $"{type.QualifiedName}.{name}", LanguageId.CSharp,
            $"M:{type.QualifiedName}.{name}", [SourceLocation.Line(file.Id, 2, 1, 10)], containingEntity: type);
        _entities.Add(entity);
        return entity;
    }

    public ExternalDependency Package(string name)
    {
        var dependency = ExternalDependency.Create(Repository, PackageEcosystem.NuGet, name);
        _dependencies.Add(dependency);
        return dependency;
    }

    public CodeRelationship Relate(
        ElementKind sourceKind, ElementId source, RelationshipKind kind, ElementKind targetKind, ElementId target,
        params EvidenceLevel[] levels)
    {
        var sourceRef = new ElementReference(sourceKind, source);
        var targetRef = new ElementReference(targetKind, target);
        var evidenceFile = _files[0].Id;
        var relationship = CodeRelationship.Create(sourceRef, kind, targetRef,
            (levels.Length == 0 ? [EvidenceLevel.Confirmed] : levels).Select((level, index) => AnalysisEvidence.Create(
                CodeRelationship.IdFor(sourceRef, kind, targetRef), AnalyzerId.Create("test.analyzer"), "1", level,
                $"test.rule{index}", SourceLocation.Line(evidenceFile, index + 1, 1, 5), Run)));
        _relationships.Add(relationship);
        return relationship;
    }

    public CodeRelationship ProjectReference(CodeProject source, CodeProject target, EvidenceLevel level = EvidenceLevel.Confirmed) =>
        Relate(ElementKind.Project, source.Id, RelationshipKind.ProjectReference, ElementKind.Project, target.Id, level);

    public ArchitectureModel Build()
    {
        var model = ArchitectureModel.Create(Repository, Run, _files, _projects, _modules, _entities, _dependencies, _relationships);
        model.EnsureValid();
        return model;
    }

    private RepositoryFile File(string path, FileClassification classification, ElementId? projectId = null) =>
        RepositoryFile.Create(Repository, RelativePath.Create(path), LanguageId.CSharp, classification, 10, Hash, projectId, 1);
}
