namespace CodebaseExplorer.Analysis.FileSystem;

/// <summary>Path comparisons used for containment checks.</summary>
internal static class PathContainment
{
    /// <summary>
    /// Windows and macOS file systems are case-insensitive by default; Linux file systems are
    /// case-sensitive. Containment checks follow the platform default.
    /// </summary>
    public static StringComparison Comparison { get; } =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    /// <summary>Absolute path without trailing separators (except for file-system roots).</summary>
    public static string Normalize(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full) ?? string.Empty;

        return full.Length > root.Length
            ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            : full;
    }

    /// <summary>True if <paramref name="path"/> equals <paramref name="directory"/> or lies below it.</summary>
    public static bool IsWithin(string path, string directory)
    {
        var normalizedPath = Normalize(path);
        var normalizedDirectory = Normalize(directory);

        if (string.Equals(normalizedPath, normalizedDirectory, Comparison))
        {
            return true;
        }

        var prefix = normalizedDirectory.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedDirectory
            : normalizedDirectory + Path.DirectorySeparatorChar;

        return normalizedPath.StartsWith(prefix, Comparison);
    }
}
