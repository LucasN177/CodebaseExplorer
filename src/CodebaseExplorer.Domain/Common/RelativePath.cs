namespace CodebaseExplorer.Domain.Common;

/// <summary>
/// A normalized path relative to the repository root: <c>/</c> separators, no leading <c>./</c>,
/// no empty segments, case preserved. Rooted paths and <c>..</c> segments are rejected, so a
/// <see cref="RelativePath"/> can never point outside the repository.
/// </summary>
public readonly record struct RelativePath
{
    private RelativePath(string value) => Value = value;

    /// <summary>The repository root itself.</summary>
    public static RelativePath Root { get; } = new(string.Empty);

    /// <summary>Normalized path; empty for <see cref="Root"/>.</summary>
    public string Value => field ?? string.Empty;

    public bool IsRoot => Value.Length == 0;

    /// <summary>Last path segment; empty for <see cref="Root"/>.</summary>
    public string FileName => IsRoot ? string.Empty : Value[(Value.LastIndexOf('/') + 1)..];

    /// <summary>
    /// Validates and normalizes <paramref name="path"/>. <c>"."</c> denotes <see cref="Root"/>.
    /// </summary>
    /// <exception cref="ArgumentException">The path is empty, rooted, or contains <c>..</c> segments.</exception>
    public static RelativePath Create(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var unified = path.Replace('\\', '/');
        if (unified.StartsWith('/') || IsDriveQualified(unified))
        {
            throw new ArgumentException($"'{path}' is rooted; a repository-relative path is required.", nameof(path));
        }

        if (unified.Split('/').Any(segment => segment == ".."))
        {
            throw new ArgumentException($"'{path}' contains a '..' segment.", nameof(path));
        }

        if (unified.Trim('/') is "." or "")
        {
            return Root;
        }

        return new RelativePath(NaturalKey.NormalizePath(unified, nameof(path)));
    }

    /// <summary>
    /// True if this path equals <paramref name="directory"/> or lies below it (segment-wise, ordinal).
    /// Every path lies within <see cref="Root"/>.
    /// </summary>
    public bool IsWithin(RelativePath directory) =>
        directory.IsRoot
        || Value == directory.Value
        || Value.StartsWith(directory.Value + "/", StringComparison.Ordinal);

    public override string ToString() => IsRoot ? "." : Value;

    private static bool IsDriveQualified(string path) =>
        path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':';
}
