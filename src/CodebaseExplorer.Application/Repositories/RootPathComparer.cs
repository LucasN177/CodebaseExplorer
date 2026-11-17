namespace CodebaseExplorer.Application.Repositories;

/// <summary>
/// Compares repository root paths the way the platform's default file system does: case-insensitive
/// on Windows and macOS, case-sensitive on Linux.
/// </summary>
internal static class RootPathComparer
{
    public static StringComparer Instance { get; } =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
