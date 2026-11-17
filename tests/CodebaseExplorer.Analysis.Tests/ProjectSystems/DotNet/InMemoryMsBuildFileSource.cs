using CodebaseExplorer.Analysis.ProjectSystems.DotNet;
using CodebaseExplorer.Domain.Common;

namespace CodebaseExplorer.Analysis.Tests.ProjectSystems.DotNet;

/// <summary>MSBuild files held in memory, keyed by repository-relative path.</summary>
internal sealed class InMemoryMsBuildFileSource(Dictionary<string, string> files) : IMsBuildFileSource
{
    public bool Exists(RelativePath path) => files.ContainsKey(path.Value);

    public Task<(string? Content, string? Error)> LoadAsync(RelativePath path, CancellationToken cancellationToken) =>
        Task.FromResult(files.TryGetValue(path.Value, out var content) ? (content, (string?)null) : ((string?)null, "not found"));
}
