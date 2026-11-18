using CodebaseExplorer.Application.Abstractions;
using CodebaseExplorer.Application.Common;
using CodebaseExplorer.Application.Git;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Repositories;
using CodebaseExplorer.TestRepositories;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace CodebaseExplorer.IntegrationTests.Application;

/// <summary>
/// GIT-003: clone → analyze through the real composition root and SQLite store. The cloner is a test double that
/// "clones" a fixture into the target (real clones are covered by <c>GitRemoteClonerTests</c>; network tests are opt-in).
/// </summary>
public sealed class ImportRemoteRepositoryTests : IAsyncLifetime
{
    private const string Url = "https://github.com/Example/Layered.git";

    private readonly TestApplication _app = new();
    private readonly IGitRemoteCloner _cloner = Substitute.For<IGitRemoteCloner>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string ClonePath => Path.Combine(Get<IApplicationPaths>().ClonesDirectory, "github.com", "example", "layered");

    public ValueTask InitializeAsync() => _app.InitializeAsync();

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    [Fact]
    public async Task Clone_IsAnalyzedAndRegisteredAsAClone()
    {
        CloneSucceedsWithFixture();

        var result = await UseCase().ExecuteAsync(new RemoteImportRequest("https://me:ghp_secret@github.com/Example/Layered.git", "main", "ghp_token", ExistingCloneAction.Ask), null, null, Ct);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        var import = result.Value;
        Assert.Equal(Url, import.RemoteUrl);
        Assert.Equal(ClonePath, import.ClonePath);
        Assert.True(import.CredentialsRemovedFromUrl);
        Assert.False(import.Updated);
        Assert.True(import.Summary.ProjectCount > 0);

        var repository = await Get<IArchitectureModelStore>().FindRepositoryAsync(RepositoryIdOf(import.Summary.RepositoryId), Ct);
        Assert.Equal(RepositorySourceKind.GitHub, repository!.Location.SourceKind);
        Assert.Equal(new Uri(Url), repository.Location.RemoteUrl);
        Assert.Equal(ClonePath, repository.Location.LocalPath);

        await _cloner.Received(1).CloneAsync(
            Arg.Is<GitCloneRequest>(r => r.RemoteUrl == new Uri(Url) && r.TargetPath == ClonePath && r.Branch == "main"
                && r.Token!.UserName == "x-access-token" && r.Token.Secret == "ghp_token"),
            Arg.Any<IProgress<GitProgress>?>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("ssh://git@github.com/example/layered.git", GitErrorCodes.UrlSchemeNotSupported)]
    [InlineData("file:///C:/repo", GitErrorCodes.UrlSchemeNotSupported)]
    [InlineData("https://github.com/example", GitErrorCodes.UrlInvalid)]
    public async Task InvalidUrl_IsRejectedBeforeCloning(string url, string code)
    {
        var result = await UseCase().ExecuteAsync(new RemoteImportRequest(url, null, null, ExistingCloneAction.Ask), null, null, Ct);

        Assert.Equal(code, result.Error.Code);
        Assert.Empty(_cloner.ReceivedCalls());
    }

    [Fact]
    public async Task CancelledClone_RegistersNothing()
    {
        _cloner.CloneAsync(Arg.Any<GitCloneRequest>(), Arg.Any<IProgress<GitProgress>?>(), Arg.Any<CancellationToken>()).Returns<Task<Result<GitCloneResult>>>(_ => throw new OperationCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => UseCase().ExecuteAsync(new RemoteImportRequest(Url, null, null, ExistingCloneAction.Ask), null, null, Ct));

        Assert.Empty(await Get<IArchitectureModelStore>().ListRepositoriesAsync(Ct));
    }

    [Fact]
    public async Task FailedClone_RegistersNothing()
    {
        _cloner.CloneAsync(Arg.Any<GitCloneRequest>(), Arg.Any<IProgress<GitProgress>?>(), Arg.Any<CancellationToken>()).Returns(
            Task.FromResult<Result<GitCloneResult>>(ApplicationError.Failure(GitErrorCodes.AuthenticationFailed, "rejected")));

        var result = await UseCase().ExecuteAsync(new RemoteImportRequest(Url, null, null, ExistingCloneAction.Ask), null, null, Ct);

        Assert.Equal(GitErrorCodes.AuthenticationFailed, result.Error.Code);
        Assert.Empty(await Get<IArchitectureModelStore>().ListRepositoriesAsync(Ct));
    }

    [Fact]
    public async Task ExistingClone_Ask_ReturnsTheConflict()
    {
        CloneExists();

        var result = await UseCase().ExecuteAsync(new RemoteImportRequest(Url, null, null, ExistingCloneAction.Ask), null, null, Ct);

        Assert.Equal(GitErrorCodes.CloneExists, result.Error.Code);
        await _cloner.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default, default, default, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExistingClone_Update_UpdatesAndAnalyzes()
    {
        CloneExists();
        CopyFixtureToClone();
        _cloner.UpdateAsync(ClonePath, "dev", null, Arg.Any<IProgress<GitProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Result<GitCloneResult>>(new GitCloneResult(ClonePath, "dev", null, false)));

        var result = await UseCase().ExecuteAsync(new RemoteImportRequest(Url, " dev ", "  ", ExistingCloneAction.Update), null, null, Ct);

        Assert.True(result.Value.Updated);
        Assert.Equal("dev", result.Value.Branch);
    }

    [Fact]
    public async Task ExistingClone_Reclone_DeletesThenClones()
    {
        var calls = 0;
        _cloner.CloneAsync(Arg.Any<GitCloneRequest>(), Arg.Any<IProgress<GitProgress>?>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (calls++ == 0)
            {
                return Task.FromResult<Result<GitCloneResult>>(ApplicationError.Conflict(GitErrorCodes.CloneExists, "exists"));
            }

            CopyFixtureToClone();
            return Task.FromResult<Result<GitCloneResult>>(new GitCloneResult(ClonePath, "main", null, true));
        });
        _cloner.DeleteCloneAsync(ClonePath, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Success()));

        var result = await UseCase().ExecuteAsync(new RemoteImportRequest(Url, null, null, ExistingCloneAction.Reclone), null, null, Ct);

        Assert.True(result.Value.HasSubmodules);
        Received.InOrder(() =>
        {
            _cloner.CloneAsync(Arg.Any<GitCloneRequest>(), Arg.Any<IProgress<GitProgress>?>(), Arg.Any<CancellationToken>());
            _cloner.DeleteCloneAsync(ClonePath, Arg.Any<CancellationToken>());
            _cloner.CloneAsync(Arg.Any<GitCloneRequest>(), Arg.Any<IProgress<GitProgress>?>(), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task RemoveRepository_DeletesTheCloneDirectoryButNeverALocalFolder()
    {
        CloneSucceedsWithFixture();
        var cloned = (await UseCase().ExecuteAsync(new RemoteImportRequest(Url, null, null, ExistingCloneAction.Ask), null, null, Ct)).Value;
        using var local = ZeroExecutionHarnessCopy();
        var localImport = (await Get<ImportLocalRepository>().ExecuteAsync(local.FullPath, null, Ct)).Value;

        Assert.True((await Get<RemoveRepository>().ExecuteAsync(cloned.Summary.RepositoryId, Ct)).IsSuccess);
        Assert.True((await Get<RemoveRepository>().ExecuteAsync(localImport.RepositoryId, Ct)).IsSuccess);

        Assert.False(Directory.Exists(ClonePath));
        Assert.True(Directory.Exists(local.FullPath));
        Assert.NotEmpty(Directory.EnumerateFiles(local.FullPath, "*", SearchOption.AllDirectories));
    }

    private static TempDirectory ZeroExecutionHarnessCopy() => Security.ZeroExecutionHarness.CopyFixture("dotnet-layered");

    private ImportRemoteRepository UseCase() => new(_cloner, Get<IApplicationPaths>(), Get<ImportLocalRepository>());

    private T Get<T>()
        where T : notnull => _app.Services.GetRequiredService<T>();

    private void CloneSucceedsWithFixture() =>
        _cloner.CloneAsync(Arg.Any<GitCloneRequest>(), Arg.Any<IProgress<GitProgress>?>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            CopyFixtureToClone();
            return Task.FromResult<Result<GitCloneResult>>(new GitCloneResult(ClonePath, "main", null, false));
        });

    private void CloneExists() =>
        _cloner.CloneAsync(Arg.Any<GitCloneRequest>(), Arg.Any<IProgress<GitProgress>?>(), Arg.Any<CancellationToken>()).Returns(
            Task.FromResult<Result<GitCloneResult>>(ApplicationError.Conflict(GitErrorCodes.CloneExists, "exists")));

    private void CopyFixtureToClone()
    {
        var source = TestRepositoryCatalog.GetFixturePath("dotnet-layered");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(ClonePath, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }

    private static RepositoryId RepositoryIdOf(string id) => new(Guid.ParseExact(id, "N"));
}
