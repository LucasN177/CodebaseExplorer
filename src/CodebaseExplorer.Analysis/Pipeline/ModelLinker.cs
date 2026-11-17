using CodebaseExplorer.Analysis.Scanning;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Files;
using CodebaseExplorer.Domain.Model;
using CodebaseExplorer.Domain.Projects;
using CodebaseExplorer.Domain.Relationships;

namespace CodebaseExplorer.Analysis.Pipeline;

/// <summary>
/// Stage 4 (ANALYSIS_ENGINE.md): merges analyzer fragments and scanned files into one
/// <see cref="ArchitectureModel"/>.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Elements with the same id and natural key are merged (first occurrence wins).</item>
/// <item>Relationships with the same id are merged into one relationship with all evidence.</item>
/// <item>Elements with the same id but different natural keys are all kept, so that model
/// validation reports the collision and the run fails.</item>
/// <item>Each file is attached to the innermost project whose root directory contains it;
/// placeholder projects for missing references never own files.</item>
/// </list>
/// Fragments must be passed in a deterministic order (the pipeline orders them by analyzer id).
/// </remarks>
internal static class ModelLinker
{
    public static ArchitectureModel Link(
        RepositoryId repositoryId,
        AnalysisRunId analysisRunId,
        IReadOnlyList<ScannedFile> scannedFiles,
        IReadOnlyList<AnalyzerFragment> fragments)
    {
        ArgumentNullException.ThrowIfNull(scannedFiles);
        ArgumentNullException.ThrowIfNull(fragments);

        var projects = Merge(fragments.SelectMany(f => f.Projects), p => p.Id, p => p.NaturalKey);
        var modules = Merge(fragments.SelectMany(f => f.Modules), m => m.Id, m => m.NaturalKey);
        var entities = Merge(fragments.SelectMany(f => f.Entities), e => e.Id, e => e.NaturalKey);
        var dependencies = Merge(fragments.SelectMany(f => f.ExternalDependencies), d => d.Id, d => d.NaturalKey);
        var relationships = MergeRelationships(fragments.SelectMany(f => f.Relationships));

        var owners = projects
            .Where(p => !p.IsPlaceholder())
            .OrderByDescending(p => p.RootDirectory.Value.Length)
            .ThenBy(p => p.NaturalKey.Value, StringComparer.Ordinal)
            .ToArray();

        var files = scannedFiles.Select(file => RepositoryFile.Create(
            repositoryId,
            file.Path,
            file.Language,
            file.Classification,
            file.SizeBytes,
            file.Hash,
            owners.FirstOrDefault(p => file.Path.IsWithin(p.RootDirectory))?.Id,
            file.LineCount));

        return ArchitectureModel.Create(repositoryId, analysisRunId, files, projects, modules, entities, dependencies, relationships);
    }

    private static List<T> Merge<T>(IEnumerable<T> elements, Func<T, ElementId> idOf, Func<T, NaturalKey> keyOf)
    {
        var result = new List<T>();
        var keysById = new Dictionary<ElementId, NaturalKey>();

        foreach (var element in elements)
        {
            var id = idOf(element);
            if (keysById.TryGetValue(id, out var existing) && existing == keyOf(element))
            {
                continue; // same element produced again
            }

            keysById.TryAdd(id, keyOf(element));
            result.Add(element); // new element, or a colliding one that validation will report
        }

        return result;
    }

    private static List<CodeRelationship> MergeRelationships(IEnumerable<CodeRelationship> relationships)
    {
        var result = new List<CodeRelationship>();
        var indexById = new Dictionary<ElementId, int>();

        foreach (var relationship in relationships)
        {
            if (indexById.TryGetValue(relationship.Id, out var index)
                && result[index].NaturalKey == relationship.NaturalKey)
            {
                result[index] = result[index].WithEvidence(relationship.Evidence, relationship.Attributes);
                continue;
            }

            indexById.TryAdd(relationship.Id, result.Count);
            result.Add(relationship);
        }

        return result;
    }
}
