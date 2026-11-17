namespace CodebaseExplorer.Domain.Repositories;

/// <summary>
/// Location of a repository: the absolute local working-tree path and, for cloned repositories,
/// the remote URL. Remote URLs never contain credentials (SECURITY.md).
/// </summary>
public sealed record RepositoryLocation
{
    private RepositoryLocation(string localPath, Uri? remoteUrl, RepositorySourceKind sourceKind)
    {
        LocalPath = localPath;
        RemoteUrl = remoteUrl;
        SourceKind = sourceKind;
    }

    /// <summary>Absolute, normalized local path without trailing separator.</summary>
    public string LocalPath { get; }

    /// <summary>Remote URL for cloned repositories; <see langword="null"/> for <see cref="RepositorySourceKind.Local"/>.</summary>
    public Uri? RemoteUrl { get; }

    public RepositorySourceKind SourceKind { get; }

    /// <summary>A repository that exists only on the local disk.</summary>
    public static RepositoryLocation Local(string localPath) =>
        new(NormalizeLocalPath(localPath), null, RepositorySourceKind.Local);

    /// <summary>A repository cloned from <paramref name="remoteUrl"/> into <paramref name="localPath"/>.</summary>
    /// <exception cref="ArgumentException">The URL is relative or contains user information.</exception>
    public static RepositoryLocation Cloned(string localPath, Uri remoteUrl, RepositorySourceKind sourceKind)
    {
        ArgumentNullException.ThrowIfNull(remoteUrl);

        if (sourceKind == RepositorySourceKind.Local || !Enum.IsDefined(sourceKind))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceKind), sourceKind, "A cloned repository needs a remote source kind.");
        }

        if (!remoteUrl.IsAbsoluteUri)
        {
            throw new ArgumentException("The remote URL must be absolute.", nameof(remoteUrl));
        }

        if (!string.IsNullOrEmpty(remoteUrl.UserInfo))
        {
            throw new ArgumentException("The remote URL must not contain credentials or user information.", nameof(remoteUrl));
        }

        return new RepositoryLocation(NormalizeLocalPath(localPath), remoteUrl, sourceKind);
    }

    private static string NormalizeLocalPath(string localPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localPath);

        if (!Path.IsPathFullyQualified(localPath))
        {
            throw new ArgumentException($"'{localPath}' is not an absolute path.", nameof(localPath));
        }

        var fullPath = Path.GetFullPath(localPath);
        var root = Path.GetPathRoot(fullPath) ?? string.Empty;

        return fullPath.Length > root.Length
            ? fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            : fullPath;
    }
}
