using System.Globalization;
using CodebaseExplorer.Application.AI;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Entities;
using CodebaseExplorer.Domain.Model;
using CodebaseExplorer.Domain.Relationships;

namespace CodebaseExplorer.AI.Retrieval;

/// <summary>
/// Facts-first retrieval (AI-004, AI_ARCHITECTURE.md §2/§4): (a) facts from the stored model — the selected element and
/// its relationships with evidence, the repository's projects and their dependencies, types whose names match the
/// question; (b) lexical search over the chunk index (FTS5, <see cref="EnsureSearchIndex"/>); (c) snippets around
/// declarations and evidence locations. The result is deterministic for a given model, index and question.
/// </summary>
internal sealed class ContextRetriever(
    ISearchIndexStore index,
    IRepositoryRootValidator rootValidator,
    IRepositoryFileReader fileReader)
{
    public const int MaxScopeRelationships = 40;
    public const int MaxProjectFacts = 60;
    public const int MaxMatchedEntities = 10;
    public const int MaxSearchHits = 12;
    public const int MaxEvidenceSnippets = 5;
    public const int EvidenceContextLines = 3;

    /// <param name="stored">The repository's current model.</param>
    /// <param name="question">The user's question (or the explanation request).</param>
    /// <param name="elementId">The selected element (<c>N</c> format) the question is about, if any.</param>
    /// <param name="cancellationToken">Cancels reading files and searching.</param>
    public async Task<RetrievedContext> RetrieveAsync(StoredRepositoryModel stored, string question, string? elementId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(question);
        var model = stored.Model;
        var root = rootValidator.Validate(stored.Repository.Location.LocalPath).TryGetValue(out var validRoot, out _) ? validRoot : null;
        var source = new SnippetSource(root, fileReader);
        var terms = SearchTerms.ForQuery(question);
        var scope = elementId is { Length: 32 } && Guid.TryParseExact(elementId, "N", out var guid) ? new ElementId(guid) : (ElementId?)null;

        var facts = new List<ContextFact>();
        var snippets = new List<ContextSnippet>();

        if (scope is { } id && Reference(model, id) is { } selected)
        {
            facts.AddRange(ScopeFacts(model, selected));
            await AddDeclarationSnippetsAsync(model, source, selected, "selected element", snippets, cancellationToken);
            await AddEvidenceSnippetsAsync(model, source, selected, snippets, cancellationToken);
        }

        facts.AddRange(OverviewFacts(stored));

        var matched = MatchEntities(model, terms);
        foreach (var entity in matched)
        {
            facts.Add(EntityFact(model, entity, "matches the question"));
        }

        foreach (var entity in matched.Take(5))
        {
            await AddDeclarationSnippetsAsync(model, source, new ElementReference(ElementKind.Entity, entity.Id), $"declaration of {entity.Name}", snippets,
                cancellationToken);
        }

        if (terms.Count > 0)
        {
            var hits = await index.SearchAsync(stored.Repository.Id, SearchTerms.WithStems(terms), MaxSearchHits, cancellationToken);
            foreach (var hit in hits)
            {
                if (model.FindFile(hit.FileId) is { } file)
                {
                    Add(snippets, new ContextSnippet(file.Path.Value, hit.StartLine, hit.EndLine, file.Language.Value, hit.Text,
                        "search match", null));
                }
            }
        }

        return new RetrievedContext(facts, snippets);
    }

    private static IEnumerable<ContextFact> ScopeFacts(ArchitectureModel model, ElementReference selected)
    {
        var label = Label(model, selected);
        var kind = KindText(model, selected);
        if (selected.Kind == ElementKind.Entity && model.FindEntity(selected.Id) is { } entity)
        {
            yield return EntityFact(model, entity, "the selected element");
        }
        else
        {
            yield return new ContextFact($"the selected element is {kind} \"{label}\"", null, null, null, selected.Id.ToString());
        }

        var members = Members(model, selected).Take(30).ToList();
        if (members.Count > 0)
        {
            yield return new ContextFact($"{kind} \"{label}\" contains: {string.Join(", ", members)}", null, null, null, selected.Id.ToString());
        }

        var related = model.Relationships
            .Where(r => r.Source.Id == selected.Id || r.Target.Id == selected.Id)
            .OrderBy(r => r.Source.Id == selected.Id ? 0 : 1)
            .ThenBy(r => r.Kind.ToKeyToken(), StringComparer.Ordinal)
            .ThenByDescending(r => r.Level)
            .ThenBy(r => Label(model, r.Source.Id == selected.Id ? r.Target : r.Source), StringComparer.Ordinal)
            .Take(MaxScopeRelationships);
        foreach (var relationship in related)
        {
            yield return RelationshipFact(model, relationship);
        }
    }

    private static IEnumerable<ContextFact> OverviewFacts(StoredRepositoryModel stored)
    {
        var model = stored.Model;
        var languages = model.Files
            .GroupBy(f => f.Language.Value)
            .Where(g => g.Key != LanguageId.Unknown.Value)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => $"{g.Key} ({g.Count().ToString(CultureInfo.InvariantCulture)} {(g.Count() == 1 ? "file" : "files")})");
        yield return new ContextFact(
            $"repository \"{stored.Repository.Name}\" has {model.Projects.Count.ToString(CultureInfo.InvariantCulture)} projects and " +
            $"{model.Files.Count.ToString(CultureInfo.InvariantCulture)} files; languages: {string.Join(", ", languages)}",
            null, null, null, null);

        var projects = model.Projects.OrderBy(p => p.Name, StringComparer.Ordinal).Take(MaxProjectFacts).ToList();
        foreach (var project in projects)
        {
            var folder = project.RootDirectory.IsRoot ? "the repository root" : project.RootDirectory.Value;
            var file = project.ProjectFilePath is { } path ? $", project file {path.Value}" : string.Empty;
            yield return new ContextFact(
                $"project \"{project.Name}\" ({project.Ecosystem.ToKeyToken()}, {project.PrimaryLanguage.Value}, {project.Kind}) in {folder}{file}",
                null, null, null, project.Id.ToString());
        }

        var projectIds = projects.Select(p => p.Id).ToHashSet();
        var dependencies = model.Relationships
            .Where(r => r.Source.Kind == ElementKind.Project && projectIds.Contains(r.Source.Id)
                && r.Kind is RelationshipKind.ProjectReference or RelationshipKind.PackageReference or RelationshipKind.FrameworkReference)
            .OrderBy(r => Label(model, r.Source), StringComparer.Ordinal)
            .ThenBy(r => r.Kind.ToKeyToken(), StringComparer.Ordinal)
            .ThenBy(r => Label(model, r.Target), StringComparer.Ordinal)
            .Take(MaxProjectFacts);
        foreach (var relationship in dependencies)
        {
            yield return RelationshipFact(model, relationship);
        }
    }

    private static List<CodeEntity> MatchEntities(ArchitectureModel model, IReadOnlyList<string> terms)
    {
        if (terms.Count == 0)
        {
            return [];
        }

        var exact = terms.ToHashSet(StringComparer.Ordinal);
        var stems = terms.Select(SearchTerms.Stem).ToHashSet(StringComparer.Ordinal);
        return [.. model.Entities
            .Where(e => e.ContainingEntityId is null)
            .Select(e => (Entity: e, Score: Score(e.Name)))
            .Where(m => m.Score > 0)
            .OrderByDescending(m => m.Score)
            .ThenBy(m => m.Entity.QualifiedName, StringComparer.Ordinal)
            .Take(MaxMatchedEntities)
            .Select(m => m.Entity)];

        // The whole name asked for wins; otherwise count the name's words that occur (stemmed) in the question.
        int Score(string name)
        {
            if (exact.Contains(name.ToLowerInvariant()))
            {
                return 100;
            }

            var parts = SearchTerms.ForQuery(name).Skip(1).DefaultIfEmpty(name.ToLowerInvariant()).Distinct();
            return parts.Count(part => stems.Contains(SearchTerms.Stem(part)));
        }
    }

    private static ContextFact EntityFact(ArchitectureModel model, CodeEntity entity, string why)
    {
        var project = model.FindProject(entity.ProjectId)?.Name ?? "?";
        var declaration = entity.Declarations.Count > 0 ? entity.Declarations[0] : (SourceLocation?)null;
        var file = declaration is { } d ? model.FindFile(d.FileId) : null;
        var text = $"{KindText(entity.Kind)} \"{entity.QualifiedName}\" in project \"{project}\" ({entity.Visibility}) — {why}";
        if (entity.Signature is { Length: > 0 } signature)
        {
            text += $"; signature: {signature}";
        }

        return new ContextFact(text, file?.Path.Value, declaration?.StartLine, declaration?.EndLine, entity.Id.ToString());
    }

    private static ContextFact RelationshipFact(ArchitectureModel model, CodeRelationship relationship)
    {
        var evidence = relationship.Evidence.Where(e => e.Location is not null)
            .OrderBy(e => model.FindFile(e.Location!.Value.FileId)?.Path.Value, StringComparer.Ordinal)
            .ThenBy(e => e.Location!.Value.StartLine)
            .FirstOrDefault();
        var location = evidence?.Location;
        var file = location is { } l ? model.FindFile(l.FileId) : null;
        var occurrences = relationship.Occurrences > 1 ? $", {relationship.Occurrences.ToString(CultureInfo.InvariantCulture)} occurrences" : string.Empty;
        return new ContextFact(
            $"{KindText(model, relationship.Source)} \"{Label(model, relationship.Source)}\" {Verb(relationship.Kind)} " +
            $"{KindText(model, relationship.Target)} \"{Label(model, relationship.Target)}\" ({relationship.Kind}, {relationship.Level}{occurrences})",
            file?.Path.Value, location?.StartLine, location?.EndLine, relationship.Id.ToString());
    }

    private static async Task AddDeclarationSnippetsAsync(ArchitectureModel model, SnippetSource source, ElementReference element, string reason,
        List<ContextSnippet> snippets, CancellationToken cancellationToken)
    {
        if (element.Kind != ElementKind.Entity || model.FindEntity(element.Id) is not { } entity)
        {
            return;
        }

        foreach (var declaration in entity.Declarations.Take(3))
        {
            // Declarations locate the name; the body length comes from the metrics (one declaration: the whole type).
            var end = declaration.EndLine > declaration.StartLine
                ? declaration.EndLine
                : declaration.StartLine + Math.Max(1, entity.Declarations.Count == 1 ? entity.Metrics?.LineCount ?? 20 : 20) - 1;
            if (model.FindFile(declaration.FileId) is { } file
                && await source.ReadAsync(file, declaration.StartLine, end, cancellationToken) is { } read)
            {
                Add(snippets, new ContextSnippet(file.Path.Value, read.Start, read.End, file.Language.Value, read.Text, reason, entity.Id.ToString()));
            }
        }
    }

    private static async Task AddEvidenceSnippetsAsync(ArchitectureModel model, SnippetSource source, ElementReference element,
        List<ContextSnippet> snippets, CancellationToken cancellationToken)
    {
        var locations = model.Relationships
            .Where(r => r.Source.Id == element.Id || r.Target.Id == element.Id)
            .SelectMany(r => r.Evidence)
            .Where(e => e.Location is not null)
            .Select(e => e.Location!.Value)
            .Distinct()
            .OrderBy(l => model.FindFile(l.FileId)?.Path.Value, StringComparer.Ordinal)
            .ThenBy(l => l.StartLine)
            .Take(MaxEvidenceSnippets);
        foreach (var location in locations)
        {
            if (model.FindFile(location.FileId) is { } file
                && await source.ReadAsync(file, Math.Max(1, location.StartLine - EvidenceContextLines), location.EndLine + EvidenceContextLines,
                    cancellationToken) is { } read)
            {
                Add(snippets, new ContextSnippet(file.Path.Value, read.Start, read.End, file.Language.Value, read.Text, "evidence of a relationship", null));
            }
        }
    }

    /// <summary>Adds the snippet unless earlier snippets of the same file already contain most of its lines.</summary>
    private static void Add(List<ContextSnippet> snippets, ContextSnippet snippet)
    {
        var sameFile = snippets.Where(s => string.Equals(s.Path, snippet.Path, StringComparison.Ordinal)).ToList();
        var length = snippet.EndLine - snippet.StartLine + 1;
        var newLines = Enumerable.Range(snippet.StartLine, length).Count(line => !sameFile.Any(s => s.StartLine <= line && line <= s.EndLine));
        if (newLines >= Math.Max(1, Math.Min(5, length)) && newLines * 10 >= length * 3)
        {
            snippets.Add(snippet);
        }
    }

    private static IEnumerable<string> Members(ArchitectureModel model, ElementReference element) => element.Kind switch
    {
        ElementKind.Project => model.Modules.Where(m => m.ProjectId == element.Id && m.ParentModuleId is null)
            .Select(m => m.QualifiedName).Order(StringComparer.Ordinal),
        ElementKind.Module => model.Modules.Where(m => m.ParentModuleId == element.Id).Select(m => m.QualifiedName)
            .Concat(model.Entities.Where(e => e.ModuleId == element.Id && e.ContainingEntityId is null).Select(e => e.Name))
            .Order(StringComparer.Ordinal),
        ElementKind.Entity => model.Entities.Where(e => e.ContainingEntityId == element.Id).Select(e => e.Name).Order(StringComparer.Ordinal),
        _ => [],
    };

    private static ElementReference? Reference(ArchitectureModel model, ElementId id)
    {
        if (model.FindProject(id) is not null)
        {
            return new ElementReference(ElementKind.Project, id);
        }

        if (model.FindModule(id) is not null)
        {
            return new ElementReference(ElementKind.Module, id);
        }

        if (model.FindEntity(id) is not null)
        {
            return new ElementReference(ElementKind.Entity, id);
        }

        if (model.FindExternalDependency(id) is not null)
        {
            return new ElementReference(ElementKind.ExternalDependency, id);
        }

        return model.FindFile(id) is not null ? new ElementReference(ElementKind.File, id) : null;
    }

    internal static string Label(ArchitectureModel model, ElementReference reference) => reference.Kind switch
    {
        ElementKind.Project => model.FindProject(reference.Id)?.Name,
        ElementKind.File => model.FindFile(reference.Id)?.Path.Value,
        ElementKind.Module => model.FindModule(reference.Id)?.QualifiedName,
        ElementKind.Entity => model.FindEntity(reference.Id)?.QualifiedName,
        ElementKind.ExternalDependency => model.FindExternalDependency(reference.Id)?.DisplayName,
        _ => null,
    } ?? reference.Id.ToString();

    private static string KindText(ArchitectureModel model, ElementReference reference) => reference.Kind switch
    {
        ElementKind.Project => "project",
        ElementKind.File => "file",
        ElementKind.Module => model.FindModule(reference.Id)?.Kind.ToKeyToken() ?? "module",
        ElementKind.Entity => model.FindEntity(reference.Id) is { } entity ? KindText(entity.Kind) : "type",
        ElementKind.ExternalDependency => "package",
        _ => "element",
    };

    private static string KindText(EntityKind kind) => kind.ToString().ToLowerInvariant();

    private static string Verb(RelationshipKind kind) => kind switch
    {
        RelationshipKind.ProjectReference => "references",
        RelationshipKind.PackageReference => "depends on",
        RelationshipKind.FrameworkReference => "uses",
        RelationshipKind.Imports => "imports",
        RelationshipKind.Inherits => "inherits from",
        RelationshipKind.Implements => "implements",
        RelationshipKind.UsesType => "uses",
        RelationshipKind.Instantiates => "instantiates",
        RelationshipKind.Calls => "calls",
        RelationshipKind.RegistersService => "registers",
        _ => "depends on",
    };
}
