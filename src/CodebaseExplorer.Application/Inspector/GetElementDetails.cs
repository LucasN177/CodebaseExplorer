using System.Globalization;
using CodebaseExplorer.Application.Common;
using CodebaseExplorer.Application.Git;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Contracts.Inspector;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Entities;
using CodebaseExplorer.Domain.Evidence;
using CodebaseExplorer.Domain.Model;
using CodebaseExplorer.Domain.Modules;
using CodebaseExplorer.Domain.Projects;
using CodebaseExplorer.Domain.Relationships;

namespace CodebaseExplorer.Application.Inspector;

/// <summary>
/// Use case: the Component Inspector's details of a project, module, entity or external dependency (UI-006,
/// USER_WORKFLOWS.md WF-04), computed from the stored model.
/// </summary>
/// <remarks>
/// A project, module or type stands for its whole subtree (files, namespaces, types, members): its relationships are
/// those crossing the subtree's boundary, so the inspector matches the aggregated graph edges. Each related element
/// carries what "show in graph" needs (<see cref="ElementReferenceDto.GraphNodeId"/>, <see cref="ElementReferenceDto.ExpandPath"/>).
/// </remarks>
public sealed class GetElementDetails(IArchitectureModelStore store, IGitActivityStore gitStore)
{
    /// <summary>At most this many declaration locations are listed as facts.</summary>
    public const int MaxDeclarations = 5;

    public async Task<Result<ElementDetailsDto>> ExecuteAsync(string repositoryId, string elementId, CancellationToken cancellationToken)
    {
        if (!RepositoryContractMapping.TryParseRepositoryId(repositoryId, out var id))
        {
            return ApplicationError.Validation(RepositoryErrorCodes.IdInvalid, "The repository id is not a valid id.");
        }

        if (await store.LoadCurrentModelAsync(id, cancellationToken) is not { } stored)
        {
            return ApplicationError.NotFound(RepositoryErrorCodes.NotAnalyzed, "The repository has not been analyzed yet.");
        }

        var model = stored.Model;
        if (elementId is not { Length: 32 } || !Guid.TryParseExact(elementId, "N", out _))
        {
            return ApplicationError.Validation(InspectorErrorCodes.ElementInvalid, "The element id is not a valid id.");
        }

        var element = ElementId.Parse(elementId);
        if (Describe(model, element) is not { } details)
        {
            return ApplicationError.NotFound(InspectorErrorCodes.ElementNotFound, "The element is not part of the current model.");
        }

        // Same aggregation as the graph overlay (GitActivityIndex), so both show the same numbers.
        var git = await gitStore.LoadCurrentAsync(id, cancellationToken);
        if (git is null || new GitActivityIndex(model, git).For(element) is not { } activity)
        {
            return details;
        }

        return details with
        {
            Activity = new ElementActivityDto
            {
                FileChanges = activity.FileChanges,
                ChangedFiles = activity.ChangedFiles,
                LinesAdded = activity.LinesAdded,
                LinesDeleted = activity.LinesDeleted,
                LastChangeAt = activity.LastChangeAt,
                DistinctAuthors = activity.DistinctAuthors,
                CommitsScanned = git.Snapshot.HistoryDepth,
                WindowStart = git.Snapshot.HistoryWindowStart,
                HeadCommitSha = git.Snapshot.HeadCommitSha,
                Branch = git.Snapshot.Branch,
            },
        };
    }

    private static ElementDetailsDto? Describe(ArchitectureModel model, ElementId id)
    {
        if (model.FindProject(id) is { } project)
        {
            return Build(model, ElementKind.Project, id, project.Name, project.IsPlaceholder() ? "Project (project file missing)" : "Project",
                ProjectFacts(model, project), model.Files.Count(f => f.ProjectId == id),
                model.Modules.Where(m => m.ProjectId == id && m.ParentModuleId is null).Select(m => Reference(model, new ElementReference(ElementKind.Module, m.Id))),
                reference => ProjectOf(model, reference) == id);
        }

        if (model.FindModule(id) is { } module)
        {
            var modules = Descendants(model, module).Select(m => m.Id).ToHashSet();
            return Build(model, ElementKind.Module, id, module.QualifiedName, "Namespace", ModuleFacts(model, module),
                model.Entities.Count(e => modules.Contains(e.ModuleId)),
                model.Entities.Where(e => e.ModuleId == id && e.ContainingEntityId is null).OrderBy(e => e.Name, StringComparer.Ordinal)
                    .Select(e => Reference(model, new ElementReference(ElementKind.Entity, e.Id))),
                reference => reference.Kind switch
                {
                    ElementKind.Module => modules.Contains(reference.Id),
                    ElementKind.Entity => model.FindEntity(reference.Id) is { } entity && modules.Contains(entity.ModuleId),
                    _ => false,
                });
        }

        if (model.FindEntity(id) is { } entityElement)
        {
            return Build(model, ElementKind.Entity, id, entityElement.Name, KindText(entityElement.Kind), EntityFacts(model, entityElement),
                entityElement.Metrics?.LineCount ?? 0,
                model.Entities.Where(e => e.ContainingEntityId == id).OrderBy(e => e.Name, StringComparer.Ordinal)
                    .Select(e => Reference(model, new ElementReference(ElementKind.Entity, e.Id))),
                reference => reference.Kind == ElementKind.Entity && IsWithin(model, reference.Id, id));
        }

        if (model.FindExternalDependency(id) is { } dependency)
        {
            return Build(model, ElementKind.ExternalDependency, id, dependency.DisplayName, "External dependency",
                [new("Ecosystem", dependency.Ecosystem.ToKeyToken()), new("Normalized name", dependency.Name)], 0, [],
                reference => reference.Kind == ElementKind.ExternalDependency && reference.Id == id);
        }

        return null;
    }

    private static ElementDetailsDto Build(
        ArchitectureModel model,
        ElementKind kind,
        ElementId id,
        string name,
        string kindText,
        IReadOnlyList<ElementFactDto> facts,
        int size,
        IEnumerable<ElementReferenceDto> members,
        Func<ElementReference, bool> inside)
    {
        var incoming = new List<(CodeRelationship Relationship, ElementReference Other, ElementReference Inner)>();
        var outgoing = new List<(CodeRelationship Relationship, ElementReference Other, ElementReference Inner)>();
        foreach (var relationship in model.Relationships)
        {
            var sourceInside = inside(relationship.Source);
            var targetInside = inside(relationship.Target);
            if (sourceInside && !targetInside)
            {
                outgoing.Add((relationship, relationship.Target, relationship.Source));
            }
            else if (targetInside && !sourceInside)
            {
                incoming.Add((relationship, relationship.Source, relationship.Target));
            }
        }

        return new ElementDetailsDto
        {
            Id = id.ToString(),
            Kind = KindToken(kind),
            Name = name,
            KindText = kindText,
            Facts = facts,
            FanIn = incoming.Select(r => r.Other).Distinct().Count(),
            FanOut = outgoing.Select(r => r.Other).Distinct().Count(),
            Size = size,
            Members = [.. members],
            Incoming = Group(model, id, incoming),
            Outgoing = Group(model, id, outgoing),
        };
    }

    private static List<RelationshipGroupDto> Group(
        ArchitectureModel model, ElementId inspected, List<(CodeRelationship Relationship, ElementReference Other, ElementReference Inner)> relationships) =>
        [.. relationships
            .GroupBy(r => r.Relationship.Kind)
            .OrderBy(g => g.Key.ToKeyToken(), StringComparer.Ordinal)
            .Select(g => new RelationshipGroupDto(
                g.Key.ToKeyToken(),
                [.. g
                    .OrderByDescending(r => r.Relationship.Level)
                    .ThenBy(r => Label(model, r.Other), StringComparer.Ordinal)
                    .ThenBy(r => Label(model, r.Inner), StringComparer.Ordinal)
                    .Select(r => new RelatedElementDto(
                        Reference(model, r.Other),
                        LevelToken(r.Relationship.Level),
                        r.Relationship.Occurrences,
                        r.Inner.Id == inspected ? null : Label(model, r.Inner)))]))];

    /// <summary>A related element with the graph node that represents it and the expansions that make that node visible.</summary>
    private static ElementReferenceDto Reference(ArchitectureModel model, ElementReference reference)
    {
        var label = Label(model, reference);
        switch (reference.Kind)
        {
            case ElementKind.Project:
                return new(reference.Id.ToString(), "project", label, reference.Id.ToString(), []);
            case ElementKind.ExternalDependency:
                return new(reference.Id.ToString(), "external-dependency", label, reference.Id.ToString(), []);
            case ElementKind.File:
                var owner = model.FindFile(reference.Id)?.ProjectId;
                return new(reference.Id.ToString(), "file", label, owner?.ToString(), []);
            case ElementKind.Module when model.FindModule(reference.Id) is { } module:
                return new(reference.Id.ToString(), "module", label, module.Id.ToString(),
                    [module.ProjectId.ToString(), .. Ancestors(model, module).Select(m => m.Id.ToString())]);
            case ElementKind.Entity when model.FindEntity(reference.Id) is { } entity:
                var outermost = entity;
                while (outermost.ContainingEntityId is { } container && model.FindEntity(container) is { } parent)
                {
                    outermost = parent;
                }

                var path = new List<string> { entity.ProjectId.ToString() };
                if (model.FindModule(outermost.ModuleId) is { } typeModule)
                {
                    path.AddRange(Ancestors(model, typeModule).Select(m => m.Id.ToString()));
                    path.Add(typeModule.Id.ToString());
                }

                return new(reference.Id.ToString(), "entity", label, outermost.Id.ToString(), path);
            default:
                return new(reference.Id.ToString(), "unknown", label, null, []);
        }
    }

    private static string Label(ArchitectureModel model, ElementReference reference) => reference.Kind switch
    {
        ElementKind.Project => model.FindProject(reference.Id)?.Name,
        ElementKind.File => model.FindFile(reference.Id)?.Path.Value,
        ElementKind.Module => model.FindModule(reference.Id)?.QualifiedName,
        ElementKind.Entity => model.FindEntity(reference.Id)?.QualifiedName,
        ElementKind.ExternalDependency => model.FindExternalDependency(reference.Id)?.DisplayName,
        _ => null,
    } ?? reference.Id.ToString();

    private static List<ElementFactDto> ProjectFacts(ArchitectureModel model, CodeProject project)
    {
        var facts = new List<ElementFactDto>
        {
            new("Folder", project.RootDirectory.Value.Length == 0 ? "(repository root)" : project.RootDirectory.Value),
            new("Ecosystem", project.Ecosystem.ToKeyToken()),
            new("Language", project.PrimaryLanguage.Value),
        };
        if (project.ProjectFilePath is { } file)
        {
            facts.Add(new("Project file", file.Value));
        }

        var frameworks = project.Properties.GetValueOrDefault("TargetFrameworks") ?? project.Properties.GetValueOrDefault("TargetFramework");
        if (frameworks is { Length: > 0 })
        {
            facts.Add(new("Target framework(s)", frameworks));
        }

        if (project.IsPlaceholder())
        {
            facts.Add(new("Referenced file (missing)", project.Properties.GetValueOrDefault(ProjectPropertyKeys.ReferencedPath) ?? "?"));
        }

        facts.Add(new("Namespaces", model.Modules.Count(m => m.ProjectId == project.Id).ToString(CultureInfo.InvariantCulture)));
        facts.Add(new("Types", model.Entities.Count(e => e.ProjectId == project.Id && e.ContainingEntityId is null).ToString(CultureInfo.InvariantCulture)));
        return facts;
    }

    private static List<ElementFactDto> ModuleFacts(ArchitectureModel model, CodeModule module) =>
    [
        new("Project", model.FindProject(module.ProjectId)?.Name ?? "?"),
        new("Kind", module.Kind.ToKeyToken()),
        new("Sub-namespaces", model.Modules.Count(m => m.ParentModuleId == module.Id).ToString(CultureInfo.InvariantCulture)),
    ];

    private static List<ElementFactDto> EntityFacts(ArchitectureModel model, CodeEntity entity)
    {
        var facts = new List<ElementFactDto>
        {
            new("Qualified name", entity.QualifiedName),
            new("Project", model.FindProject(entity.ProjectId)?.Name ?? "?"),
            new("Namespace", model.FindModule(entity.ModuleId)?.QualifiedName ?? "?"),
            new("Visibility", entity.Visibility.ToString()),
        };
        if (entity.Modifiers != EntityModifiers.None)
        {
            facts.Add(new("Modifiers", entity.Modifiers.ToString()));
        }

        if (entity.Signature is { Length: > 0 } signature)
        {
            facts.Add(new("Signature", signature));
        }

        foreach (var declaration in entity.Declarations.Take(MaxDeclarations))
        {
            facts.Add(new("Declared in", $"{model.FindFile(declaration.FileId)?.Path.Value ?? "?"}:{declaration.StartLine.ToString(CultureInfo.InvariantCulture)}"));
        }

        facts.Add(new("Symbol key", entity.SymbolKey));
        return facts;
    }

    private static ElementId? ProjectOf(ArchitectureModel model, ElementReference reference) => reference.Kind switch
    {
        ElementKind.Project => reference.Id,
        ElementKind.File => model.FindFile(reference.Id)?.ProjectId,
        ElementKind.Module => model.FindModule(reference.Id)?.ProjectId,
        ElementKind.Entity => model.FindEntity(reference.Id)?.ProjectId,
        _ => null,
    };

    private static bool IsWithin(ArchitectureModel model, ElementId entity, ElementId ancestor)
    {
        for (var current = model.FindEntity(entity); current is not null; current = current.ContainingEntityId is { } next ? model.FindEntity(next) : null)
        {
            if (current.Id == ancestor)
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<CodeModule> Descendants(ArchitectureModel model, CodeModule module)
    {
        yield return module;
        foreach (var child in model.Modules.Where(m => m.ParentModuleId == module.Id))
        {
            foreach (var descendant in Descendants(model, child))
            {
                yield return descendant;
            }
        }
    }

    /// <summary>Parent modules, outermost first (excluding the module itself).</summary>
    private static List<CodeModule> Ancestors(ArchitectureModel model, CodeModule module)
    {
        var chain = new List<CodeModule>();
        for (var current = module.ParentModuleId is { } parent ? model.FindModule(parent) : null; current is not null;
            current = current.ParentModuleId is { } next ? model.FindModule(next) : null)
        {
            chain.Insert(0, current);
        }

        return chain;
    }

    private static string KindToken(ElementKind kind) => kind switch
    {
        ElementKind.Project => "project",
        ElementKind.Module => "module",
        ElementKind.Entity => "entity",
        ElementKind.ExternalDependency => "external-dependency",
        _ => "file",
    };

    private static string KindText(EntityKind kind) => kind switch
    {
        EntityKind.TypeAlias => "Type alias",
        _ => kind.ToString(),
    };

    private static string LevelToken(EvidenceLevel level) => level switch
    {
        EvidenceLevel.Confirmed => "confirmed",
        EvidenceLevel.Syntactic => "syntactic",
        _ => "heuristic",
    };
}
