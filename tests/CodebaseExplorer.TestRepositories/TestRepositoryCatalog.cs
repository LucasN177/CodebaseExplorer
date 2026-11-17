namespace CodebaseExplorer.TestRepositories;

/// <summary>
/// Locates fixture repositories stored in <c>tests/CodebaseExplorer.TestRepositories/Repositories</c>.
/// </summary>
/// <remarks>
/// Fixtures are read directly from the source tree (not copied to the build output) so that their
/// file layout is exactly what is committed. Tests that need to mutate a fixture must copy it to a
/// temporary directory first.
/// </remarks>
public static class TestRepositoryCatalog
{
    private const string SolutionFileName = "CodebaseExplorer.slnx";

    private static readonly Lazy<string> RepositoryRootLazy = new(FindRepositoryRoot);

    /// <summary>Absolute path of the Codebase Explorer repository root (the folder containing the .slnx).</summary>
    public static string RepositoryRoot => RepositoryRootLazy.Value;

    /// <summary>Absolute path of the folder that contains all fixture repositories.</summary>
    public static string FixturesRoot =>
        Path.Combine(RepositoryRoot, "tests", "CodebaseExplorer.TestRepositories", "Repositories");

    /// <summary>Returns the absolute path of a fixture repository and fails if it does not exist.</summary>
    public static string GetFixturePath(string fixtureName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fixtureName);

        var path = Path.Combine(FixturesRoot, fixtureName);
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"Fixture repository '{fixtureName}' was not found at '{path}'.");
        }

        return path;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate '{SolutionFileName}' above '{AppContext.BaseDirectory}'.");
    }
}
