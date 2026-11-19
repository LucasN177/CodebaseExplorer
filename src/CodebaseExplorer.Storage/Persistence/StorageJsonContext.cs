using System.Text.Json.Serialization;

namespace CodebaseExplorer.Storage.Persistence;

/// <summary>JSON shape of <c>AnalysisRuns.StatisticsJson</c>; levels as enum names, durations as <c>c</c>-format strings.</summary>
internal sealed record StatisticsJson(
    int FilesScanned,
    int FilesSkipped,
    int Projects,
    int Modules,
    int Entities,
    int Relationships,
    int ExternalDependencies,
    Dictionary<string, int> RelationshipsByLevel,
    Dictionary<string, TimeSpan> StageDurations);

/// <summary>JSON shape of one entry of <c>Projects.SolutionMembershipsJson</c>.</summary>
internal sealed record SolutionMembershipJson(string SolutionFileId, string? SolutionFolderPath);

/// <summary>JSON shape of one entry of <c>GitSnapshots.RemotesJson</c> (sanitized URL).</summary>
internal sealed record GitRemoteJson(string Name, string Url);

/// <summary>JSON shape of one entry of <c>AIMessages.CitationsJson</c> (AI-006).</summary>
internal sealed record AiCitationJson(string Text, string? Key, string? Path, int? StartLine, int? EndLine, string? ElementId, bool Verified, string? Problem);

/// <summary>JSON shape of <c>AIMessages.ContextManifestJson</c> (AI-006).</summary>
internal sealed record AiManifestJson(List<AiContextEntryJson> Entries, int BudgetTokens, int UsedTokens, int OmittedEntries);

/// <summary>One context entry of <see cref="AiManifestJson"/>.</summary>
internal sealed record AiContextEntryJson(string Key, string Kind, string Label, string? Path, int? StartLine, int? EndLine, string? ElementId, int Tokens);

/// <summary>Source-generated metadata for the JSON columns (DATABASE_SCHEMA.md §1 "JSON").</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(StatisticsJson))]
[JsonSerializable(typeof(List<SolutionMembershipJson>))]
[JsonSerializable(typeof(List<GitRemoteJson>))]
[JsonSerializable(typeof(List<AiCitationJson>))]
[JsonSerializable(typeof(AiManifestJson))]
internal sealed partial class StorageJsonContext : JsonSerializerContext;
