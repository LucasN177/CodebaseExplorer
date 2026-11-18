using System.Runtime.CompilerServices;
using CodebaseExplorer.Application.Common;
using CodebaseExplorer.Application.Inspector;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Contracts.Search;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Model;

namespace CodebaseExplorer.Application.Search;

/// <summary>
/// Use case: find projects, modules, code elements and packages by name (GRAPH-007, <c>Ctrl/Cmd+K</c>,
/// USER_WORKFLOWS.md WF-05). Case-insensitive; exact names first, then prefixes, then names containing the text, then
/// qualified names containing it; shorter names first. Each result carries the graph node and expansions that
/// <c>Show in graph</c> needs.
/// </summary>
/// <remarks>
/// Searches the current model of the repository, which the model store caches per run (ADR-010); the lowercase name
/// index is built once per loaded model. Measured on the 2,000-project performance fixture in QA-002/GRAPH-007
/// (TEST_STRATEGY.md §8).
/// </remarks>
public sealed class SearchElements(IArchitectureModelStore store)
{
    public const int DefaultLimit = 50;

    public const int MaxLimit = 200;

    private static readonly ConditionalWeakTable<ArchitectureModel, Entry[]> Indexes = [];

    public async Task<Result<SearchResultsDto>> ExecuteAsync(string repositoryId, string text, int limit = DefaultLimit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (!RepositoryContractMapping.TryParseRepositoryId(repositoryId, out var id))
        {
            return ApplicationError.Validation(RepositoryErrorCodes.IdInvalid, "The repository id is not a valid id.");
        }

        if (limit is < 1 or > MaxLimit)
        {
            return ApplicationError.Validation(SearchErrorCodes.LimitInvalid, $"The result limit must be between 1 and {MaxLimit}.");
        }

        var term = text.Trim().ToLowerInvariant();
        if (term.Length == 0)
        {
            return new SearchResultsDto([], 0);
        }

        if (await store.LoadCurrentModelAsync(id, cancellationToken) is not { } stored)
        {
            return ApplicationError.NotFound(RepositoryErrorCodes.NotAnalyzed, "The repository has not been analyzed yet.");
        }

        var model = stored.Model;
        var index = Indexes.GetValue(model, BuildIndex);
        var matches = new List<(int Score, Entry Entry)>();
        foreach (var entry in index)
        {
            var score = entry.Name == term ? 0
                : entry.Name.StartsWith(term, StringComparison.Ordinal) ? 1
                : entry.Name.Contains(term, StringComparison.Ordinal) ? 2
                : entry.Qualified.Contains(term, StringComparison.Ordinal) ? 3
                : -1;
            if (score >= 0)
            {
                matches.Add((score, entry));
            }
        }

        var items = matches
            .OrderBy(m => m.Score)
            .ThenBy(m => m.Entry.Name.Length)
            .ThenBy(m => m.Entry.KindOrder)
            .ThenBy(m => m.Entry.Qualified, StringComparer.Ordinal)
            .Take(limit)
            .Select(m => new SearchResultDto(GetElementDetails.Reference(model, m.Entry.Reference), m.Entry.KindText, m.Entry.Container))
            .ToList();
        return new SearchResultsDto(items, matches.Count);
    }

    /// <summary>One searchable element with lowercase name and qualified name.</summary>
    private sealed record Entry(string Name, string Qualified, int KindOrder, ElementReference Reference, string KindText, string? Container);

    private static Entry[] BuildIndex(ArchitectureModel model)
    {
        var entries = new List<Entry>(model.Projects.Count + model.Modules.Count + model.Entities.Count + model.ExternalDependencies.Count);
        foreach (var project in model.Projects)
        {
            entries.Add(new Entry(project.Name.ToLowerInvariant(), project.RootDirectory.Value.ToLowerInvariant(), 0,
                new ElementReference(ElementKind.Project, project.Id), "Project", project.RootDirectory.ToString()));
        }

        foreach (var module in model.Modules)
        {
            entries.Add(new Entry(module.Name.ToLowerInvariant(), module.QualifiedName.ToLowerInvariant(), 1,
                new ElementReference(ElementKind.Module, module.Id), GetElementDetails.ModuleKindText(module.Kind),
                model.FindProject(module.ProjectId)?.Name));
        }

        foreach (var entity in model.Entities)
        {
            var project = model.FindProject(entity.ProjectId)?.Name;
            var module = model.FindModule(entity.ModuleId)?.QualifiedName;
            entries.Add(new Entry(entity.Name.ToLowerInvariant(), entity.QualifiedName.ToLowerInvariant(), entity.ContainingEntityId is null ? 2 : 3,
                new ElementReference(ElementKind.Entity, entity.Id), GetElementDetails.KindText(entity.Kind),
                module is null ? project : $"{project} · {module}"));
        }

        foreach (var dependency in model.ExternalDependencies)
        {
            entries.Add(new Entry(dependency.DisplayName.ToLowerInvariant(), dependency.Name.ToLowerInvariant(), 4,
                new ElementReference(ElementKind.ExternalDependency, dependency.Id), $"Package ({dependency.Ecosystem.ToKeyToken()})", null));
        }

        return [.. entries];
    }
}

/// <summary>Error codes of <see cref="SearchElements"/>.</summary>
public static class SearchErrorCodes
{
    public const string LimitInvalid = "search.limit-invalid";
}
