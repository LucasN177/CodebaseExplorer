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

/// <summary>Source-generated metadata for the JSON columns (DATABASE_SCHEMA.md §1 "JSON").</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(StatisticsJson))]
[JsonSerializable(typeof(List<SolutionMembershipJson>))]
[JsonSerializable(typeof(List<GitRemoteJson>))]
internal sealed partial class StorageJsonContext : JsonSerializerContext;
