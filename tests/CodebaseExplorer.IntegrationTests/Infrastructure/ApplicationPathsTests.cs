using CodebaseExplorer.Infrastructure.FileSystem;

namespace CodebaseExplorer.IntegrationTests.Infrastructure;

public sealed class ApplicationPathsTests
{
    [Fact]
    public void Constructor_WithExplicitRoot_PlacesAllPathsBelowRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "ce-paths-test");

        var paths = new ApplicationPaths(root);

        Assert.Equal(Path.GetFullPath(root), paths.DataDirectory);
        Assert.StartsWith(paths.DataDirectory, paths.DatabaseFilePath, StringComparison.Ordinal);
        Assert.StartsWith(paths.DataDirectory, paths.ClonesDirectory, StringComparison.Ordinal);
        Assert.StartsWith(paths.DataDirectory, paths.LogsDirectory, StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultConstructor_UsesApplicationFolderName()
    {
        var paths = new ApplicationPaths();

        Assert.Equal(ApplicationPaths.ApplicationFolderName, Path.GetFileName(paths.DataDirectory));
    }

    [Fact]
    public void Constructor_DoesNotCreateDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ce-paths-{Guid.NewGuid():N}");

        _ = new ApplicationPaths(root);

        Assert.False(Directory.Exists(root));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsBlankRoot(string root) =>
        Assert.ThrowsAny<ArgumentException>(() => new ApplicationPaths(root));
}
