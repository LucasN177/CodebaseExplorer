namespace CodebaseExplorer.Application.Repositories;

/// <summary>
/// The validated, absolute root directory of a repository on the local disk. Obtain it from
/// <see cref="IRepositoryRootValidator"/>; all repository file access is confined to it.
/// </summary>
public sealed record RepositoryRoot
{
    /// <param name="fullPath">Absolute, normalized path without trailing separator.</param>
    public RepositoryRoot(string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);

        if (!Path.IsPathFullyQualified(fullPath))
        {
            throw new ArgumentException($"'{fullPath}' is not an absolute path.", nameof(fullPath));
        }

        FullPath = fullPath;
    }

    public string FullPath { get; }

    public override string ToString() => FullPath;
}
