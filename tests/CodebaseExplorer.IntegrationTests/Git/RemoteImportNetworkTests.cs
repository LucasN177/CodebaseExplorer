using CodebaseExplorer.Application.Git;
using Microsoft.Extensions.DependencyInjection;

namespace CodebaseExplorer.IntegrationTests.Git;

/// <summary>
/// GIT-003 end to end over the network (opt-in, <c>Category=Network</c>): clone → analyze of small public GitHub and
/// GitLab repositories through the real composition root. Run with <c>--explicit only</c>.
/// </summary>
public sealed class RemoteImportNetworkTests : IAsyncLifetime
{
    private readonly TestApplication _app = new();

    public ValueTask InitializeAsync() => _app.InitializeAsync();

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    [Theory(Explicit = true)]
    [Trait("Category", "Network")]
    [InlineData("https://github.com/libgit2/TestGitRepository")]
    [InlineData("https://gitlab.com/gitlab-examples/clojure-web-application")]
    public async Task CloneAndAnalyze_PublicRepository(string url)
    {
        var useCase = _app.Services.GetRequiredService<ImportRemoteRepository>();

        var result = await useCase.ExecuteAsync(new RemoteImportRequest(url, null, null, ExistingCloneAction.Ask), null, null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        Assert.True(Directory.Exists(Path.Combine(result.Value.ClonePath, ".git")));
        Assert.True(result.Value.Summary.FileCount > 0);

        var again = await useCase.ExecuteAsync(new RemoteImportRequest(url, null, null, ExistingCloneAction.Update), null, null, TestContext.Current.CancellationToken);
        Assert.True(again.IsSuccess, again.IsFailure ? again.Error.ToString() : null);
        Assert.True(again.Value.Updated);
    }
}
