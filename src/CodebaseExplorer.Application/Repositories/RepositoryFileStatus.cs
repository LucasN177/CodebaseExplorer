using CodebaseExplorer.Domain.Common;

namespace CodebaseExplorer.Application.Repositories;

/// <summary>A repository file that passed the reader's containment checks.</summary>
/// <param name="Path">The repository-relative path that was requested.</param>
/// <param name="SizeBytes">Current file size.</param>
/// <param name="LastWriteTimeUtc">Last modification time.</param>
public sealed record RepositoryFileStatus(RelativePath Path, long SizeBytes, DateTimeOffset LastWriteTimeUtc);
