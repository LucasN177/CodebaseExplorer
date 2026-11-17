using System.Collections.Concurrent;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Domain.Common;

namespace CodebaseExplorer.Analysis.ProjectSystems.DotNet;

/// <summary>
/// <see cref="IMsBuildFileSource"/> over the scanned repository: existence comes from the scan, content
/// from <see cref="IRepositoryFileReader"/> with the manifest size limit. Loaded files are cached, so
/// shared <c>Directory.*.props</c> files are read once per run.
/// </summary>
internal sealed class RepositoryMsBuildFileSource(
    RepositoryRoot root,
    IRepositoryFileReader reader,
    IEnumerable<RelativePath> scannedPaths,
    long maxBytes) : IMsBuildFileSource
{
    private readonly HashSet<string> _existing = scannedPaths.Select(p => p.Value).ToHashSet(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Task<(string? Content, string? Error)>> _cache = new(StringComparer.Ordinal);

    public bool Exists(RelativePath path) => _existing.Contains(path.Value);

    public Task<(string? Content, string? Error)> LoadAsync(RelativePath path, CancellationToken cancellationToken) =>
        _cache.GetOrAdd(path.Value, _ => LoadCoreAsync(path, cancellationToken));

    private async Task<(string? Content, string? Error)> LoadCoreAsync(RelativePath path, CancellationToken cancellationToken)
    {
        var result = await reader.ReadAllTextAsync(root, path, maxBytes, cancellationToken);
        return result.TryGetValue(out var content, out var error) ? (content, null) : (null, $"{error.Message} ({error.Code})");
    }
}
