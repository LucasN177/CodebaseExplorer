using CodebaseExplorer.Analysis.FileSystem;
using CodebaseExplorer.Domain.Common;

namespace CodebaseExplorer.Analysis.Tests.FileSystem;

public sealed class RepositoryPathsTests
{
    [Theory]
    [InlineData("src/A", @"..\B\B.csproj", "src/B/B.csproj")]
    [InlineData("src/A", "./Sub/x.cs", "src/A/Sub/x.cs")]
    [InlineData("src/A", "../../root.props", "root.props")]
    [InlineData(".", "a//b.csproj", "a/b.csproj")]
    [InlineData("src", "..", ".")]
    public void Resolve_HandlesSeparatorsDotsAndParents(string baseDirectory, string path, string expected) =>
        Assert.Equal(expected, RepositoryPaths.Resolve(RelativePath.Create(baseDirectory), path)!.Value.ToString());

    [Theory]
    [InlineData("src", "../../outside.csproj")]
    [InlineData(".", "../x.csproj")]
    [InlineData("src", "/etc/passwd")]
    [InlineData("src", @"C:\x.csproj")]
    [InlineData("src", "  ")]
    public void Resolve_OutsideOrRooted_ReturnsNull(string baseDirectory, string path) =>
        Assert.Null(RepositoryPaths.Resolve(RelativePath.Create(baseDirectory), path));

    [Theory]
    [InlineData("Shop.sln", ".")]
    [InlineData("src/A/A.csproj", "src/A")]
    public void DirectoryOf_ReturnsContainingDirectory(string file, string expected) =>
        Assert.Equal(expected, RepositoryPaths.DirectoryOf(RelativePath.Create(file)).ToString());
}
