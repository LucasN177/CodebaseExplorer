using CodebaseExplorer.Analysis.FileSystem;
using CodebaseExplorer.Application.Abstractions;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.TestRepositories;
using NSubstitute;

namespace CodebaseExplorer.Analysis.Tests.FileSystem;

public sealed class RepositoryRootValidatorTests : IDisposable
{
    private readonly TempDirectory _workspace = new();
    private readonly RepositoryRootValidator _validator;

    public RepositoryRootValidatorTests()
    {
        var paths = Substitute.For<IApplicationPaths>();
        paths.DataDirectory.Returns(_workspace.PathOf("app-data"));
        paths.ClonesDirectory.Returns(_workspace.PathOf("app-data/clones"));
        _validator = new RepositoryRootValidator(paths);
    }

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public void ExistingFolder_IsAcceptedAndNormalized()
    {
        var folder = _workspace.CreateDirectory("repo");

        var result = _validator.Validate(folder + Path.DirectorySeparatorChar);

        Assert.Equal(Path.GetFullPath(folder), result.Value.FullPath);
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative/folder")]
    public void NonAbsolutePath_IsInvalid(string path) =>
        Assert.Equal(RepositoryErrorCodes.PathInvalid, _validator.Validate(path).Error.Code);

    [Fact]
    public void MissingFolder_IsNotFound() =>
        Assert.Equal(RepositoryErrorCodes.PathNotFound, _validator.Validate(_workspace.PathOf("missing")).Error.Code);

    [Fact]
    public void File_IsNotADirectory()
    {
        var file = _workspace.WriteText("file.txt", "x");

        Assert.Equal(RepositoryErrorCodes.NotADirectory, _validator.Validate(file).Error.Code);
    }

    [Fact]
    public void FileSystemRoot_IsRejected()
    {
        var root = Path.GetPathRoot(_workspace.FullPath)!;

        Assert.Equal(RepositoryErrorCodes.FileSystemRoot, _validator.Validate(root).Error.Code);
    }

    [Fact]
    public void FolderInsideAppData_IsRejected()
    {
        var inside = _workspace.CreateDirectory("app-data/logs/r");

        Assert.Equal(RepositoryErrorCodes.OverlapsAppData, _validator.Validate(inside).Error.Code);
    }

    [Fact]
    public void AppManagedClone_IsAccepted()
    {
        var clone = _workspace.CreateDirectory("app-data/clones/github.com/o/r");

        Assert.Equal(Path.GetFullPath(clone), _validator.Validate(clone).Value.FullPath);
    }

    [Fact]
    public void ClonesDirectoryItself_IsRejected()
    {
        var clones = _workspace.CreateDirectory("app-data/clones");

        Assert.Equal(RepositoryErrorCodes.OverlapsAppData, _validator.Validate(clones).Error.Code);
    }

    [Fact]
    public void FolderContainingAppData_IsRejected()
    {
        _workspace.CreateDirectory("app-data");

        Assert.Equal(RepositoryErrorCodes.OverlapsAppData, _validator.Validate(_workspace.FullPath).Error.Code);
    }

    [Theory]
    [InlineData(@"\\.\PhysicalDrive0")]
    [InlineData(@"\\?\C:\repo")]
    public void DevicePaths_AreRejected(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Device paths are Windows-specific.");
        }

        Assert.Equal(RepositoryErrorCodes.DevicePath, _validator.Validate(path).Error.Code);
    }

    [Fact]
    public void AdministrativeShare_IsRejected()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Administrative shares are Windows-specific.");
        }

        Assert.Equal(RepositoryErrorCodes.DevicePath, _validator.Validate(@"\\server\c$\repo").Error.Code);
    }

    [Fact]
    public void ErrorDetails_ContainPath()
    {
        var missing = _workspace.PathOf("missing");

        Assert.Equal(Path.GetFullPath(missing), _validator.Validate(missing).Error.Details["path"]);
    }
}
