using System.Text;
using CodebaseExplorer.Analysis.FileSystem;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Runs;
using CodebaseExplorer.TestRepositories;

namespace CodebaseExplorer.Analysis.Tests.FileSystem;

public sealed class RepositoryFileReaderTests : IDisposable
{
    private const string SymlinkSkipReason =
        "Creating symbolic links is not permitted for this account/platform (e.g. Windows without developer mode).";

    private readonly TempDirectory _workspace = new();
    private readonly RepositoryRoot _root;
    private readonly RepositoryFileReader _reader = new();

    public RepositoryFileReaderTests()
    {
        _workspace.CreateDirectory("repo");
        _root = new RepositoryRoot(_workspace.PathOf("repo"));
    }

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public async Task ReadAllTextAsync_ReadsFileInsideRoot()
    {
        _workspace.WriteText("repo/src/A.cs", "class A {}");

        var result = await _reader.ReadAllTextAsync(_root, RelativePath.Create("src/A.cs"), 1024, TestContext.Current.CancellationToken);

        Assert.Equal("class A {}", result.Value);
    }

    [Fact]
    public async Task ReadAllTextAsync_DetectsByteOrderMarks()
    {
        _workspace.WriteBytes("repo/utf16.txt", Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("Grüße")).ToArray());
        _workspace.WriteBytes("repo/utf8bom.txt", Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("Grüße")).ToArray());

        var utf16 = await _reader.ReadAllTextAsync(_root, RelativePath.Create("utf16.txt"), 1024, TestContext.Current.CancellationToken);
        var utf8 = await _reader.ReadAllTextAsync(_root, RelativePath.Create("utf8bom.txt"), 1024, TestContext.Current.CancellationToken);

        Assert.Equal("Grüße", utf16.Value);
        Assert.Equal("Grüße", utf8.Value);
    }

    [Fact]
    public async Task ReadAllTextAsync_InvalidUtf8_IsReplacedNotThrown()
    {
        _workspace.WriteBytes("repo/bad.txt", [0x41, 0xFF, 0x42]);

        var result = await _reader.ReadAllTextAsync(_root, RelativePath.Create("bad.txt"), 1024, TestContext.Current.CancellationToken);

        Assert.Equal("A�B", result.Value);
    }

    [Fact]
    public async Task ReadAllBytesAsync_FileAboveLimit_IsNotReadAndYieldsInfoDiagnostic()
    {
        _workspace.WriteBytes("repo/big.bin", new byte[2048]);

        var result = await _reader.ReadAllBytesAsync(_root, RelativePath.Create("big.bin"), 1024, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(RepositoryErrorCodes.FileTooLarge, result.Error.Code);
        Assert.Equal("2048", result.Error.Details["sizeBytes"]);

        var diagnostic = FileAccessDiagnostics.From(result.Error);
        Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity);
        Assert.Contains("big.bin", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadAllBytesAsync_FileAtLimit_IsRead()
    {
        _workspace.WriteBytes("repo/exact.bin", new byte[1024]);

        var result = await _reader.ReadAllBytesAsync(_root, RelativePath.Create("exact.bin"), 1024, TestContext.Current.CancellationToken);

        Assert.Equal(1024, result.Value.Length);
    }

    [Fact]
    public async Task MissingFile_ReturnsNotFound()
    {
        var result = await _reader.ReadAllBytesAsync(_root, RelativePath.Create("missing.cs"), 1024, TestContext.Current.CancellationToken);

        Assert.Equal(RepositoryErrorCodes.FileNotFound, result.Error.Code);
        Assert.Equal(DiagnosticSeverity.Warning, FileAccessDiagnostics.From(result.Error).Severity);
    }

    [Fact]
    public void Directory_IsNotAFile()
    {
        _workspace.CreateDirectory("repo/src");

        Assert.Equal(RepositoryErrorCodes.FileNotFound, _reader.GetStatus(_root, RelativePath.Create("src")).Error.Code);
        Assert.Equal(RepositoryErrorCodes.FileNotFound, _reader.GetStatus(_root, RelativePath.Root).Error.Code);
    }

    [Fact]
    public void Traversal_IsImpossibleThroughRelativePath()
    {
        // RelativePath rejects '..' segments, so a traversal never reaches the reader as a path.
        _workspace.WriteText("secret.txt", "secret");

        Assert.ThrowsAny<ArgumentException>(() => RelativePath.Create("../secret.txt"));
    }

    [Fact]
    public async Task SymbolicLinkToOutside_IsRefused_EvenWhenFollowingIsAllowed()
    {
        var outside = _workspace.WriteText("outside/secret.txt", "secret");
        if (!_workspace.TryCreateSymbolicLink("repo/link.txt", outside, isDirectory: false))
        {
            Assert.Skip(SymlinkSkipReason);
        }

        var following = new RepositoryFileReader(SymbolicLinkPolicy.FollowWithinRoot);

        var defaultResult = await _reader.ReadAllTextAsync(_root, RelativePath.Create("link.txt"), 1024, TestContext.Current.CancellationToken);
        var followingResult = await following.ReadAllTextAsync(_root, RelativePath.Create("link.txt"), 1024, TestContext.Current.CancellationToken);

        Assert.Equal(RepositoryErrorCodes.FileLinkNotFollowed, defaultResult.Error.Code);
        Assert.Equal(RepositoryErrorCodes.FileOutsideRoot, followingResult.Error.Code);
    }

    [Fact]
    public async Task DirectoryLinkToOutside_IsRefused()
    {
        _workspace.WriteText("outside/secret.txt", "secret");
        if (!_workspace.TryCreateSymbolicLink("repo/linked", _workspace.PathOf("outside"), isDirectory: true))
        {
            Assert.Skip(SymlinkSkipReason);
        }

        var following = new RepositoryFileReader(SymbolicLinkPolicy.FollowWithinRoot);

        var result = await following.ReadAllTextAsync(_root, RelativePath.Create("linked/secret.txt"), 1024, TestContext.Current.CancellationToken);

        Assert.Equal(RepositoryErrorCodes.FileOutsideRoot, result.Error.Code);
    }

    [Fact]
    public async Task JunctionToOutside_IsRefused()
    {
        _workspace.WriteText("outside/secret.txt", "secret");
        if (!_workspace.TryCreateJunction("repo/junction", _workspace.PathOf("outside")))
        {
            Assert.Skip("Directory junctions exist only on Windows.");
        }

        var following = new RepositoryFileReader(SymbolicLinkPolicy.FollowWithinRoot);
        var path = RelativePath.Create("junction/secret.txt");

        var refused = await _reader.ReadAllTextAsync(_root, path, 1024, TestContext.Current.CancellationToken);
        var outside = await following.ReadAllTextAsync(_root, path, 1024, TestContext.Current.CancellationToken);

        Assert.Equal(RepositoryErrorCodes.FileLinkNotFollowed, refused.Error.Code);
        Assert.Equal(RepositoryErrorCodes.FileOutsideRoot, outside.Error.Code);
    }

    [Fact]
    public async Task JunctionWithinRoot_IsFollowedOnlyWhenAllowed()
    {
        _workspace.WriteText("repo/real/a.txt", "content");
        if (!_workspace.TryCreateJunction("repo/alias", _workspace.PathOf("repo/real")))
        {
            Assert.Skip("Directory junctions exist only on Windows.");
        }

        var following = new RepositoryFileReader(SymbolicLinkPolicy.FollowWithinRoot);
        var path = RelativePath.Create("alias/a.txt");

        Assert.Equal("content", (await following.ReadAllTextAsync(_root, path, 1024, TestContext.Current.CancellationToken)).Value);
        Assert.Equal(RepositoryErrorCodes.FileLinkNotFollowed,
            (await _reader.ReadAllTextAsync(_root, path, 1024, TestContext.Current.CancellationToken)).Error.Code);
    }

    [Fact]
    public async Task SymbolicLinkWithinRoot_IsFollowedOnlyWhenAllowed()
    {
        var target = _workspace.WriteText("repo/real.txt", "content");
        if (!_workspace.TryCreateSymbolicLink("repo/alias.txt", target, isDirectory: false))
        {
            Assert.Skip(SymlinkSkipReason);
        }

        var following = new RepositoryFileReader(SymbolicLinkPolicy.FollowWithinRoot);

        var followed = await following.ReadAllTextAsync(_root, RelativePath.Create("alias.txt"), 1024, TestContext.Current.CancellationToken);
        var refused = await _reader.ReadAllTextAsync(_root, RelativePath.Create("alias.txt"), 1024, TestContext.Current.CancellationToken);

        Assert.Equal("content", followed.Value);
        Assert.Equal(RepositoryErrorCodes.FileLinkNotFollowed, refused.Error.Code);
    }

    [Fact]
    public void OpenRead_ReturnsReadableStream()
    {
        _workspace.WriteText("repo/a.txt", "abc");

        using var stream = _reader.OpenRead(_root, RelativePath.Create("a.txt")).Value;
        using var reader = new StreamReader(stream);

        Assert.Equal("abc", reader.ReadToEnd());
    }

    [Fact]
    public void GetStatus_ReportsSize()
    {
        _workspace.WriteBytes("repo/a.bin", new byte[17]);

        var status = _reader.GetStatus(_root, RelativePath.Create("a.bin")).Value;

        Assert.Equal(17, status.SizeBytes);
        Assert.Equal(RelativePath.Create("a.bin"), status.Path);
    }

    [Fact]
    public async Task Cancellation_Throws()
    {
        _workspace.WriteBytes("repo/a.bin", new byte[10]);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _reader.ReadAllBytesAsync(_root, RelativePath.Create("a.bin"), 1024, cancelled.Token));
    }
}
