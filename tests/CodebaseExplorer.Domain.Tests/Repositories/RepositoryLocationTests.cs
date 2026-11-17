using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Repositories;

namespace CodebaseExplorer.Domain.Tests.Repositories;

public sealed class RepositoryLocationTests
{
    private static readonly string AbsolutePath = Path.Combine(Path.GetTempPath(), "ce-repo");

    [Fact]
    public void Local_NormalizesAbsolutePathAndTrailingSeparator()
    {
        var location = RepositoryLocation.Local(AbsolutePath + Path.DirectorySeparatorChar);

        Assert.Equal(Path.GetFullPath(AbsolutePath), location.LocalPath);
        Assert.Null(location.RemoteUrl);
        Assert.Equal(RepositorySourceKind.Local, location.SourceKind);
    }

    [Fact]
    public void Local_KeepsFileSystemRoot()
    {
        var root = Path.GetPathRoot(Path.GetTempPath())!;

        Assert.Equal(root, RepositoryLocation.Local(root).LocalPath);
    }

    [Theory]
    [InlineData("relative/path")]
    [InlineData("")]
    public void Local_RejectsRelativeOrEmptyPaths(string path) =>
        Assert.ThrowsAny<ArgumentException>(() => RepositoryLocation.Local(path));

    [Fact]
    public void Cloned_StoresRemote()
    {
        var url = new Uri("https://github.com/org/repo.git");

        var location = RepositoryLocation.Cloned(AbsolutePath, url, RepositorySourceKind.GitHub);

        Assert.Equal(url, location.RemoteUrl);
        Assert.Equal(RepositorySourceKind.GitHub, location.SourceKind);
    }

    [Theory]
    [InlineData("https://user:token@github.com/org/repo.git")]
    [InlineData("https://token@gitlab.com/org/repo.git")]
    public void Cloned_RejectsCredentialsInUrl(string url) =>
        Assert.Throws<ArgumentException>(() =>
            RepositoryLocation.Cloned(AbsolutePath, new Uri(url), RepositorySourceKind.OtherGitRemote));

    [Fact]
    public void Cloned_RejectsRelativeUrl() =>
        Assert.Throws<ArgumentException>(() =>
            RepositoryLocation.Cloned(AbsolutePath, new Uri("org/repo", UriKind.Relative), RepositorySourceKind.GitHub));

    [Fact]
    public void Cloned_RejectsLocalSourceKind() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RepositoryLocation.Cloned(AbsolutePath, new Uri("https://github.com/o/r"), RepositorySourceKind.Local));
}
