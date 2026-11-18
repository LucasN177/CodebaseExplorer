using System.Text.Json;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Evidence;
using CodebaseExplorer.Domain.Files;
using CodebaseExplorer.Domain.Modules;
using CodebaseExplorer.Domain.Projects;
using CodebaseExplorer.Domain.Repositories;
using CodebaseExplorer.Domain.Runs;
using CodebaseExplorer.Storage.Persistence.Entities;

namespace CodebaseExplorer.Storage.Persistence;

/// <summary>
/// Maps Domain types to persistence records and back (DATABASE_SCHEMA.md §1 "Persistence entities").
/// Model elements are rebuilt through their Domain factories, which re-derive the deterministic ids;
/// a stored id or natural key that does not match the re-derived one means corrupted data or an id
/// collision and fails with <see cref="InvalidDataException"/> (DOMAIN_MODEL.md, collision detection).
/// </summary>
internal static partial class RecordMapper
{
    // ---------------------------------------------------------------- Repositories

    public static RepositoryRecord ToRecord(Repository repository, DateTimeOffset? lastOpenedAt = null) => new()
    {
        Id = repository.Id.Value,
        Name = repository.Name,
        LocalPath = repository.Location.LocalPath,
        RemoteUrl = repository.Location.RemoteUrl?.ToString(),
        SourceKind = repository.Location.SourceKind,
        AddedAt = repository.AddedAt.ToUniversalTime(),
        LastOpenedAt = lastOpenedAt?.ToUniversalTime(),
        LastAnalysisRunId = repository.LastAnalysisRunId?.Value,
    };

    public static Repository ToDomain(RepositoryRecord record)
    {
        var location = record.RemoteUrl is null
            ? RepositoryLocation.Local(record.LocalPath)
            : RepositoryLocation.Cloned(record.LocalPath, new Uri(record.RemoteUrl, UriKind.Absolute), record.SourceKind);

        return new Repository(
            new RepositoryId(record.Id),
            record.Name,
            location,
            record.AddedAt,
            record.LastAnalysisRunId is { } runId ? new AnalysisRunId(runId) : null);
    }

    // ---------------------------------------------------------------- AnalysisRuns, AnalysisDiagnostics

    public static AnalysisRunRecord ToRecord(AnalysisRun run) => new()
    {
        Id = run.Id.Value,
        RepositoryId = run.RepositoryId.Value,
        Status = run.Status,
        Trigger = run.Trigger,
        StartedAt = run.StartedAt.ToUniversalTime(),
        CompletedAt = run.CompletedAt?.ToUniversalTime(),
        CommitSha = run.CommitSha,
        Branch = run.Branch,
        AnalyzerVersionsJson = JsonSerializer.Serialize(
            run.AnalyzerVersions.ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal),
            StorageJsonContext.Default.DictionaryStringString),
        StatisticsJson = JsonSerializer.Serialize(ToJson(run.Statistics), StorageJsonContext.Default.StatisticsJson),
        Diagnostics = [.. run.Diagnostics.Select((diagnostic, ordinal) => ToRecord(run.Id, diagnostic, ordinal))],
    };

    public static AnalysisRun ToDomain(AnalysisRunRecord record)
    {
        var analyzerVersions = Deserialize(record.AnalyzerVersionsJson, StorageJsonContext.Default.DictionaryStringString, "AnalyzerVersionsJson");
        var statistics = Deserialize(record.StatisticsJson, StorageJsonContext.Default.StatisticsJson, "StatisticsJson");

        return AnalysisRun.Restore(
            new AnalysisRunId(record.Id),
            new RepositoryId(record.RepositoryId),
            record.Trigger,
            record.Status,
            record.StartedAt,
            record.CompletedAt,
            record.CommitSha,
            record.Branch,
            analyzerVersions,
            ToDomain(statistics),
            record.Diagnostics.OrderBy(d => d.Ordinal).Select(ToDomain));
    }

    private static AnalysisDiagnosticRecord ToRecord(AnalysisRunId runId, AnalysisDiagnostic diagnostic, int ordinal) => new()
    {
        AnalysisRunId = runId.Value,
        Ordinal = ordinal,
        Severity = diagnostic.Severity,
        Code = diagnostic.Code,
        Message = diagnostic.Message,
        AnalyzerId = diagnostic.Analyzer?.Value,
        FileId = diagnostic.Location?.FileId.Value,
        StartLine = diagnostic.Location?.StartLine,
        StartColumn = diagnostic.Location?.StartColumn,
        EndLine = diagnostic.Location?.EndLine,
        EndColumn = diagnostic.Location?.EndColumn,
    };

    private static AnalysisDiagnostic ToDomain(AnalysisDiagnosticRecord record)
    {
        SourceLocation? location = record is { FileId: { } fileId, StartLine: { } startLine, StartColumn: { } startColumn, EndLine: { } endLine, EndColumn: { } endColumn }
            ? new SourceLocation(new ElementId(fileId), startLine, startColumn, endLine, endColumn)
            : null;

        return new AnalysisDiagnostic(
            record.Severity,
            record.Code,
            record.Message,
            record.AnalyzerId is { } analyzer ? AnalyzerId.Create(analyzer) : null,
            location);
    }

    private static StatisticsJson ToJson(AnalysisStatistics statistics) => new(
        statistics.FilesScanned,
        statistics.FilesSkipped,
        statistics.Projects,
        statistics.Modules,
        statistics.Entities,
        statistics.Relationships,
        statistics.ExternalDependencies,
        statistics.RelationshipsByLevel.ToDictionary(l => l.Key.ToString(), l => l.Value, StringComparer.Ordinal),
        statistics.StageDurations.ToDictionary(d => d.Key, d => d.Value, StringComparer.Ordinal));

    private static AnalysisStatistics ToDomain(StatisticsJson json) => new(
        json.FilesScanned,
        json.FilesSkipped,
        json.Projects,
        json.Modules,
        json.Entities,
        json.Relationships,
        json.ExternalDependencies,
        (json.RelationshipsByLevel ?? []).ToDictionary(
            l => Enum.TryParse<EvidenceLevel>(l.Key, out var level) && Enum.IsDefined(level)
                ? level
                : throw new InvalidDataException($"Unknown evidence level '{l.Key}' in StatisticsJson."),
            l => l.Value),
        json.StageDurations ?? []);

    // ---------------------------------------------------------------- Files

    public static FileRecord ToRecord(RepositoryFile file, AnalysisRunId lastSeenRunId) => new()
    {
        Id = file.Id.Value,
        RepositoryId = file.RepositoryId.Value,
        RelativePath = file.Path.Value,
        NaturalKey = file.NaturalKey.Value,
        Language = file.Language.Value,
        Classification = file.Classification,
        SizeBytes = file.SizeBytes,
        LineCount = file.LineCount,
        ContentHash = file.Hash.Bytes.ToArray(),
        ProjectId = file.ProjectId?.Value,
        LastSeenRunId = lastSeenRunId.Value,
    };

    public static RepositoryFile ToDomain(FileRecord record)
    {
        var file = RepositoryFile.Create(
            new RepositoryId(record.RepositoryId),
            RelativePath.Create(record.RelativePath),
            LanguageId.Create(record.Language),
            record.Classification,
            record.SizeBytes,
            ContentHash.FromBytes(record.ContentHash),
            record.ProjectId is { } projectId ? new ElementId(projectId) : null,
            record.LineCount);

        return Verified(file, file.Id, file.NaturalKey, record.Id, record.NaturalKey, "Files");
    }

    // ---------------------------------------------------------------- Projects

    public static ProjectRecord ToRecord(CodeProject project, AnalysisRunId lastSeenRunId) => new()
    {
        Id = project.Id.Value,
        RepositoryId = project.RepositoryId.Value,
        Name = project.Name,
        NaturalKey = project.NaturalKey.Value,
        Kind = project.Kind,
        Ecosystem = project.Ecosystem,
        PrimaryLanguage = project.PrimaryLanguage.Value,
        ProjectFileId = project.ProjectFileId?.Value,
        RootDirectory = project.RootDirectory.Value,
        PropertiesJson = JsonSerializer.Serialize(
            new Dictionary<string, string>(project.Properties, StringComparer.Ordinal), StorageJsonContext.Default.DictionaryStringString),
        SolutionMembershipsJson = JsonSerializer.Serialize(
            project.SolutionMemberships.Select(m => new SolutionMembershipJson(m.SolutionFileId.ToString(), m.SolutionFolderPath)).ToList(),
            StorageJsonContext.Default.ListSolutionMembershipJson),
        LastSeenRunId = lastSeenRunId.Value,
    };

    /// <param name="record">The project row.</param>
    /// <param name="filePaths">Relative paths of the repository's files by id; the manifest path is looked up here.</param>
    public static CodeProject ToDomain(ProjectRecord record, IReadOnlyDictionary<Guid, string> filePaths)
    {
        RelativePath? projectFilePath = null;
        if (record.ProjectFileId is { } fileId)
        {
            projectFilePath = filePaths.TryGetValue(fileId, out var path)
                ? RelativePath.Create(path)
                : throw new InvalidDataException($"Project {record.Id:N} references a project file that is not stored.");
        }

        var memberships = Deserialize(record.SolutionMembershipsJson, StorageJsonContext.Default.ListSolutionMembershipJson, "SolutionMembershipsJson")
            .Select(m => new SolutionMembership(ElementId.Parse(m.SolutionFileId), m.SolutionFolderPath));

        var project = CodeProject.Create(
            new RepositoryId(record.RepositoryId),
            record.Name,
            record.Kind,
            record.Ecosystem,
            LanguageId.Create(record.PrimaryLanguage),
            projectFilePath,
            record.RootDirectory.Length == 0 ? RelativePath.Root : RelativePath.Create(record.RootDirectory), // the repository root is stored as ""
            Deserialize(record.PropertiesJson, StorageJsonContext.Default.DictionaryStringString, "PropertiesJson"),
            memberships);

        return Verified(project, project.Id, project.NaturalKey, record.Id, record.NaturalKey, "Projects");
    }

    // ---------------------------------------------------------------- Modules

    public static ModuleRecord ToRecord(CodeModule module, AnalysisRunId lastSeenRunId) => new()
    {
        Id = module.Id.Value,
        RepositoryId = module.RepositoryId.Value,
        ProjectId = module.ProjectId.Value,
        ParentModuleId = module.ParentModuleId?.Value,
        Name = module.Name,
        QualifiedName = module.QualifiedName,
        NaturalKey = module.NaturalKey.Value,
        Kind = module.Kind,
        LastSeenRunId = lastSeenRunId.Value,
    };

    /// <summary>Rebuilds modules parents first; every module needs its project and (if any) its parent.</summary>
    public static IReadOnlyList<CodeModule> ToDomain(IEnumerable<ModuleRecord> records, IReadOnlyDictionary<ElementId, CodeProject> projects)
    {
        var pending = records.ToList();
        var built = new Dictionary<Guid, CodeModule>();

        while (pending.Count > 0)
        {
            var ready = pending.Where(r => r.ParentModuleId is null || built.ContainsKey(r.ParentModuleId.Value)).ToList();
            if (ready.Count == 0)
            {
                throw new InvalidDataException("Modules reference parents that are not stored or form a cycle.");
            }

            foreach (var record in ready)
            {
                if (!projects.TryGetValue(new ElementId(record.ProjectId), out var project))
                {
                    throw new InvalidDataException($"Module {record.Id:N} references a project that is not stored.");
                }

                var module = CodeModule.Create(
                    project,
                    record.Kind,
                    record.Name,
                    record.QualifiedName,
                    record.ParentModuleId is { } parentId ? built[parentId] : null);
                built[record.Id] = Verified(module, module.Id, module.NaturalKey, record.Id, record.NaturalKey, "Modules");
                pending.Remove(record);
            }
        }

        return [.. built.Values];
    }

    // ---------------------------------------------------------------- helpers

    private static T Verified<T>(T element, ElementId id, NaturalKey naturalKey, Guid storedId, string storedNaturalKey, string table)
    {
        if (id.Value != storedId || naturalKey.Value != storedNaturalKey)
        {
            throw new InvalidDataException(
                $"Row {storedId:N} in {table} does not match its natural key (corrupted data or id collision).");
        }

        return element;
    }

    private static T Deserialize<T>(string json, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo, string column)
    {
        try
        {
            return JsonSerializer.Deserialize(json, typeInfo) ?? throw new InvalidDataException($"Column {column} is null JSON.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Column {column} contains invalid JSON.", exception);
        }
    }
}
