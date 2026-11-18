using System.Text.Json;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Dependencies;
using CodebaseExplorer.Domain.Entities;
using CodebaseExplorer.Domain.Evidence;
using CodebaseExplorer.Domain.Model;
using CodebaseExplorer.Domain.Modules;
using CodebaseExplorer.Domain.Projects;
using CodebaseExplorer.Domain.Relationships;
using CodebaseExplorer.Storage.Persistence.Entities;

namespace CodebaseExplorer.Storage.Persistence;

// Entities, external dependencies, relationships and evidence (STORE-002, DATABASE_SCHEMA.md §4.7–§4.11).
internal static partial class RecordMapper
{
    // ---------------------------------------------------------------- CodeEntities, CodeEntityDeclarations

    public static CodeEntityRecord ToRecord(CodeEntity entity, AnalysisRunId lastSeenRunId) => new()
    {
        Id = entity.Id.Value,
        RepositoryId = entity.RepositoryId.Value,
        ProjectId = entity.ProjectId.Value,
        ModuleId = entity.ModuleId.Value,
        ContainingEntityId = entity.ContainingEntityId?.Value,
        Name = entity.Name,
        QualifiedName = entity.QualifiedName,
        NaturalKey = entity.NaturalKey.Value,
        SymbolKey = entity.SymbolKey,
        Kind = entity.Kind,
        Language = entity.Language.Value,
        Visibility = entity.Visibility,
        Modifiers = entity.Modifiers,
        Signature = entity.Signature,
        LineCount = entity.Metrics?.LineCount,
        MemberCount = entity.Metrics?.MemberCount,
        LastSeenRunId = lastSeenRunId.Value,
        Declarations = [.. entity.Declarations.Select((location, ordinal) => new CodeEntityDeclarationRecord
        {
            EntityId = entity.Id.Value,
            Ordinal = ordinal,
            FileId = location.FileId.Value,
            StartLine = location.StartLine,
            StartColumn = location.StartColumn,
            EndLine = location.EndLine,
            EndColumn = location.EndColumn,
        })],
    };

    /// <summary>Rebuilds entities containing entities first; every entity needs its project and module.</summary>
    public static IReadOnlyList<CodeEntity> ToDomain(
        IEnumerable<CodeEntityRecord> records,
        IReadOnlyDictionary<ElementId, CodeProject> projects,
        IReadOnlyDictionary<ElementId, CodeModule> modules)
    {
        var pending = records.ToList();
        var built = new Dictionary<Guid, CodeEntity>();

        while (pending.Count > 0)
        {
            var ready = pending.Where(r => r.ContainingEntityId is null || built.ContainsKey(r.ContainingEntityId.Value)).ToList();
            if (ready.Count == 0)
            {
                throw new InvalidDataException("Entities reference containing entities that are not stored or form a cycle.");
            }

            foreach (var record in ready)
            {
                var project = projects.GetValueOrDefault(new ElementId(record.ProjectId))
                    ?? throw new InvalidDataException($"Entity {record.Id:N} references a project that is not stored.");
                var module = modules.GetValueOrDefault(new ElementId(record.ModuleId))
                    ?? throw new InvalidDataException($"Entity {record.Id:N} references a module that is not stored.");

                var entity = CodeEntity.Create(
                    project,
                    module,
                    record.Kind,
                    record.Name,
                    record.QualifiedName,
                    LanguageId.Create(record.Language),
                    record.SymbolKey,
                    record.Declarations
                        .OrderBy(d => d.Ordinal)
                        .Select(d => new SourceLocation(new ElementId(d.FileId), d.StartLine, d.StartColumn, d.EndLine, d.EndColumn)),
                    record.Visibility,
                    record.Modifiers,
                    record.ContainingEntityId is { } containingId ? built[containingId] : null,
                    record.Signature,
                    record.LineCount is null && record.MemberCount is null ? null : new EntityMetrics(record.LineCount, record.MemberCount));

                built[record.Id] = Verified(entity, entity.Id, entity.NaturalKey, record.Id, record.NaturalKey, "CodeEntities");
                pending.Remove(record);
            }
        }

        return [.. built.Values];
    }

    // ---------------------------------------------------------------- ExternalDependencies

    public static ExternalDependencyRecord ToRecord(ExternalDependency dependency, AnalysisRunId lastSeenRunId) => new()
    {
        Id = dependency.Id.Value,
        RepositoryId = dependency.RepositoryId.Value,
        Ecosystem = dependency.Ecosystem,
        Name = dependency.Name,
        DisplayName = dependency.DisplayName,
        NaturalKey = dependency.NaturalKey.Value,
        LastSeenRunId = lastSeenRunId.Value,
    };

    public static ExternalDependency ToDomain(ExternalDependencyRecord record)
    {
        var dependency = ExternalDependency.Create(new RepositoryId(record.RepositoryId), record.Ecosystem, record.DisplayName);
        return Verified(dependency, dependency.Id, dependency.NaturalKey, record.Id, record.NaturalKey, "ExternalDependencies");
    }

    // ---------------------------------------------------------------- CodeRelationships, AnalysisEvidence

    /// <summary>
    /// The relationship row with its denormalized container ids, which are taken from
    /// <paramref name="model"/> (file → project; module/entity → project and module).
    /// </summary>
    public static CodeRelationshipRecord ToRecord(CodeRelationship relationship, ArchitectureModel model, AnalysisRunId lastSeenRunId)
    {
        var (sourceProject, sourceModule) = Containers(model, relationship.Source);
        var (targetProject, targetModule) = Containers(model, relationship.Target);

        return new CodeRelationshipRecord
        {
            Id = relationship.Id.Value,
            RepositoryId = model.RepositoryId.Value,
            SourceKind = relationship.Source.Kind,
            SourceId = relationship.Source.Id.Value,
            TargetKind = relationship.Target.Kind,
            TargetId = relationship.Target.Id.Value,
            Kind = relationship.Kind,
            Level = relationship.Level,
            LevelRank = (int)relationship.Level,
            Occurrences = relationship.Occurrences,
            OriginAnalyzer = relationship.Origin.Value,
            AttributesJson = relationship.Attributes.Count == 0
                ? null
                : JsonSerializer.Serialize(new Dictionary<string, string>(relationship.Attributes, StringComparer.Ordinal), StorageJsonContext.Default.DictionaryStringString),
            SourceProjectId = sourceProject,
            TargetProjectId = targetProject,
            SourceModuleId = sourceModule,
            TargetModuleId = targetModule,
            LastSeenRunId = lastSeenRunId.Value,
        };
    }

    public static IEnumerable<AnalysisEvidenceRecord> ToEvidenceRecords(CodeRelationship relationship, RepositoryId repositoryId) =>
        relationship.Evidence.Select(evidence => new AnalysisEvidenceRecord
        {
            Id = evidence.Id.Value,
            RepositoryId = repositoryId.Value,
            RelationshipId = relationship.Id.Value,
            AnalysisRunId = evidence.AnalysisRunId.Value,
            AnalyzerId = evidence.Analyzer.Value,
            AnalyzerVersion = evidence.AnalyzerVersion,
            Level = evidence.Level,
            Rule = evidence.Rule,
            FileId = evidence.Location?.FileId.Value,
            StartLine = evidence.Location?.StartLine,
            StartColumn = evidence.Location?.StartColumn,
            EndLine = evidence.Location?.EndLine,
            EndColumn = evidence.Location?.EndColumn,
            Excerpt = evidence.Excerpt,
        });

    /// <param name="record">The relationship row.</param>
    /// <param name="evidence">All evidence rows of this relationship.</param>
    public static CodeRelationship ToDomain(CodeRelationshipRecord record, IEnumerable<AnalysisEvidenceRecord> evidence)
    {
        var source = new ElementReference(record.SourceKind, new ElementId(record.SourceId));
        var target = new ElementReference(record.TargetKind, new ElementId(record.TargetId));
        var relationshipId = CodeRelationship.IdFor(source, record.Kind, target);
        if (relationshipId.Value != record.Id)
        {
            throw new InvalidDataException($"Row {record.Id:N} in CodeRelationships does not match its endpoints and kind (corrupted data or id collision).");
        }

        var rebuiltEvidence = evidence.Select(e =>
        {
            SourceLocation? location = e is { FileId: { } fileId, StartLine: { } startLine, StartColumn: { } startColumn, EndLine: { } endLine, EndColumn: { } endColumn }
                ? new SourceLocation(new ElementId(fileId), startLine, startColumn, endLine, endColumn)
                : null;
            var rebuilt = AnalysisEvidence.Create(relationshipId, AnalyzerId.Create(e.AnalyzerId), e.AnalyzerVersion, e.Level, e.Rule, location,
                new AnalysisRunId(e.AnalysisRunId), e.Excerpt);
            return rebuilt.Id.Value == e.Id
                ? rebuilt
                : throw new InvalidDataException($"Row {e.Id:N} in AnalysisEvidence does not match its natural key (corrupted data or id collision).");
        }).ToList();

        if (rebuiltEvidence.Count == 0)
        {
            throw new InvalidDataException($"Relationship {record.Id:N} has no evidence (every relationship needs at least one, ADR-007).");
        }

        var attributes = record.AttributesJson is null
            ? null
            : Deserialize(record.AttributesJson, StorageJsonContext.Default.DictionaryStringString, "AttributesJson");

        var relationship = CodeRelationship.Create(source, record.Kind, target, rebuiltEvidence, attributes, AnalyzerId.Create(record.OriginAnalyzer));

        // All evidence is stored (no sampling), so the derived values must match the stored ones.
        if (relationship.Occurrences != record.Occurrences || relationship.Level != record.Level || (int)relationship.Level != record.LevelRank)
        {
            throw new InvalidDataException($"Relationship {record.Id:N} does not match its stored level or occurrence count.");
        }

        return relationship;
    }

    private static (Guid? Project, Guid? Module) Containers(ArchitectureModel model, ElementReference reference) =>
        reference.Kind switch
        {
            ElementKind.Project => (reference.Id.Value, null),
            ElementKind.File => (model.FindFile(reference.Id)?.ProjectId?.Value, null),
            ElementKind.Module => (model.FindModule(reference.Id)?.ProjectId.Value, reference.Id.Value),
            ElementKind.Entity => model.FindEntity(reference.Id) is { } entity ? (entity.ProjectId.Value, entity.ModuleId.Value) : (null, null),
            _ => (null, null),
        };
}
