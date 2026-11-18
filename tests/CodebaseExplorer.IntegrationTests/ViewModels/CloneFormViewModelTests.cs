using CodebaseExplorer.Application.Abstractions;
using CodebaseExplorer.Application.Common;
using CodebaseExplorer.Application.Git;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Desktop.Services;
using CodebaseExplorer.Desktop.ViewModels.Pages;
using CodebaseExplorer.TestRepositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace CodebaseExplorer.IntegrationTests.ViewModels;

/// <summary>
/// UI-008: the clone form of the start page — real use cases and store, substituted cloner (it "clones" the
/// dotnet-layered fixture). The token is never displayed after entry and never logged.
/// </summary>
public sealed class CloneFormViewModelTests : IAsyncLifetime
{
    private const string Token = "ghp_TOKEN_THAT_MUST_NEVER_APPEAR";
    private const string Url = "https://github.com/example/layered";

    private readonly TestApplication _app = new();
    private readonly IGitRemoteCloner _cloner = Substitute.For<IGitRemoteCloner>();
    private readonly RepositorySession _session = new();
    private readonly ShellStatus _shell = new(TimeProvider.System);
    private readonly ListLogger _logger = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string ClonePath => Path.Combine(_app.Services.GetRequiredService<IApplicationPaths>().ClonesDirectory, "github.com", "example", "layered");

    public ValueTask InitializeAsync() => _app.InitializeAsync();

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    [Fact]
    public async Task Clone_Success_AnalyzesOpensAndClosesTheForm()
    {
        CloneSucceeds();
        var viewModel = CreateViewModel();
        viewModel.ShowCloneFormCommand.Execute(null);
        viewModel.CloneUrl = Url;
        viewModel.CloneBranch = "main";
        viewModel.CloneToken = Token;

        await viewModel.CloneCommand.ExecuteAsync(null);

        Assert.Null(viewModel.ErrorMessage);
        Assert.StartsWith("Cloned and analyzed layered:", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.False(viewModel.IsCloneFormOpen);
        Assert.Equal(string.Empty, viewModel.CloneToken);
        Assert.Equal(ClonePath, _session.Current!.RootPath);
        await _cloner.Received(1).CloneAsync(
            Arg.Is<GitCloneRequest>(r => r.Token!.Secret == Token && r.Branch == "main"), Arg.Any<IProgress<GitProgress>?>(), Arg.Any<CancellationToken>());
        var recent = Assert.Single(viewModel.RecentRepositories);
        Assert.True(recent.IsClone);
        Assert.Equal($"Clone of {Url}", recent.SourceText);
        AssertTokenNowhere(viewModel);
    }

    [Fact]
    public async Task Token_IsEmptiedWhenTheCloneStarts()
    {
        var started = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        _cloner.CloneAsync(Arg.Any<GitCloneRequest>(), Arg.Any<IProgress<GitProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                started.SetResult();
                await release.Task;
                return (Result<GitCloneResult>)ApplicationError.Failure(GitErrorCodes.CloneFailed, "failed");
            });
        var viewModel = CreateViewModel();
        viewModel.CloneUrl = Url;
        viewModel.CloneToken = Token;

        var running = viewModel.CloneCommand.ExecuteAsync(null);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.Equal(string.Empty, viewModel.CloneToken);
        Assert.True(viewModel.IsImporting);
        release.SetResult();
        await running;
        Assert.Equal("failed", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task Failures_NeverShowOrLogTheToken()
    {
        var viewModel = CreateViewModel();
        viewModel.CloneUrl = $"https://user:{Token}@github.com/example/layered";
        viewModel.CloneToken = Token;
        _cloner.CloneAsync(Arg.Any<GitCloneRequest>(), Arg.Any<IProgress<GitProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Result<GitCloneResult>>(ApplicationError.Failure(GitErrorCodes.AuthenticationFailed, "The remote rejected the request.")));

        await viewModel.CloneCommand.ExecuteAsync(null);
        Assert.Equal("The remote rejected the request.", viewModel.ErrorMessage);
        AssertTokenNowhere(viewModel);

        // An unexpected exception whose message contains the token (e.g. echoed input) is logged without its message.
        viewModel.CloneToken = Token;
        _cloner.CloneAsync(Arg.Any<GitCloneRequest>(), Arg.Any<IProgress<GitProgress>?>(), Arg.Any<CancellationToken>())
            .Returns<Task<Result<GitCloneResult>>>(_ => throw new InvalidOperationException($"boom {Token}"));

        await viewModel.CloneCommand.ExecuteAsync(null);
        Assert.Equal("The clone failed unexpectedly. Details are in the application log.", viewModel.ErrorMessage);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Error);
        AssertTokenNowhere(viewModel);
    }

    [Fact]
    public async Task ExistingClone_OffersUpdate_AndUsesTheEnteredTokenWithoutShowingIt()
    {
        _cloner.CloneAsync(Arg.Any<GitCloneRequest>(), Arg.Any<IProgress<GitProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Result<GitCloneResult>>(ApplicationError.Conflict(GitErrorCodes.CloneExists, "exists",
                new Dictionary<string, string> { ["path"] = ClonePath })));
        _cloner.UpdateAsync(ClonePath, null, Arg.Any<GitAccessToken?>(), Arg.Any<IProgress<GitProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                CopyFixtureToClone();
                return Task.FromResult<Result<GitCloneResult>>(new GitCloneResult(ClonePath, "main", null, false));
            });
        var viewModel = CreateViewModel();
        viewModel.CloneUrl = Url;
        viewModel.CloneToken = Token;

        await viewModel.CloneCommand.ExecuteAsync(null);
        Assert.True(viewModel.HasExistingClone);
        Assert.Equal(ClonePath, viewModel.ExistingClonePath);
        Assert.Equal(string.Empty, viewModel.CloneToken);

        await viewModel.ResolveExistingCloneCommand.ExecuteAsync("update");

        Assert.False(viewModel.HasExistingClone);
        Assert.StartsWith("Updated and analyzed layered:", viewModel.StatusMessage, StringComparison.Ordinal);
        await _cloner.Received(1).UpdateAsync(ClonePath, null, Arg.Is<GitAccessToken?>(t => t!.Secret == Token),
            Arg.Any<IProgress<GitProgress>?>(), Arg.Any<CancellationToken>());
        AssertTokenNowhere(viewModel);
    }

    [Fact]
    public async Task Cancel_RegistersNothing()
    {
        _cloner.CloneAsync(Arg.Any<GitCloneRequest>(), Arg.Any<IProgress<GitProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                await Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>());
                return (Result<GitCloneResult>)new GitCloneResult(ClonePath, "main", null, false);
            });
        var viewModel = CreateViewModel();
        viewModel.CloneUrl = Url;

        var running = viewModel.CloneCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => viewModel.IsImporting);
        viewModel.CancelImportCommand.Execute(null);
        await running;

        Assert.Equal("Clone cancelled. Nothing was kept.", viewModel.StatusMessage);
        Assert.Empty(viewModel.RecentRepositories);
        Assert.Null(_session.Current);
    }

    [Fact]
    public async Task InvalidAddress_IsShownInline()
    {
        var viewModel = CreateViewModel();
        viewModel.CloneUrl = "git@github.com:example/layered.git";

        await viewModel.CloneCommand.ExecuteAsync(null);

        Assert.StartsWith("Only HTTPS remotes", viewModel.ErrorMessage, StringComparison.Ordinal);
        Assert.Empty(_cloner.ReceivedCalls());
    }

    [Fact]
    public void CloneCommand_NeedsAnAddress()
    {
        var viewModel = CreateViewModel();

        Assert.False(viewModel.CloneCommand.CanExecute(null));
        viewModel.CloneUrl = Url;
        Assert.True(viewModel.CloneCommand.CanExecute(null));
    }

    private void AssertTokenNowhere(StartPageViewModel viewModel)
    {
        Assert.DoesNotContain(_logger.Entries, e => e.Message.Contains(Token, StringComparison.Ordinal));
        foreach (var text in new[] { viewModel.StatusMessage, viewModel.ErrorMessage, viewModel.ImportPath, viewModel.CloneToken, viewModel.CloneUrl })
        {
            Assert.DoesNotContain(Token, text ?? string.Empty, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(_shell.Notifications, n => n.Message.Contains(Token, StringComparison.Ordinal));
    }

    private void CloneSucceeds() =>
        _cloner.CloneAsync(Arg.Any<GitCloneRequest>(), Arg.Any<IProgress<GitProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                CopyFixtureToClone();
                return Task.FromResult<Result<GitCloneResult>>(new GitCloneResult(ClonePath, "main", null, false));
            });

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

    private StartPageViewModel CreateViewModel()
    {
        T Get<T>()
            where T : notnull => _app.Services.GetRequiredService<T>();

        return new StartPageViewModel(
            Get<ImportLocalRepository>(),
            new ImportRemoteRepository(_cloner, Get<IApplicationPaths>(), Get<ImportLocalRepository>()),
            Get<ListRecentRepositories>(),
            Get<OpenRepository>(),
            Get<RemoveRepository>(),
            Substitute.For<IFolderPicker>(),
            _session,
            _shell,
            _logger);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var waited = 0; !condition() && waited < 5000; waited += 20)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }

    private sealed class ListLogger : ILogger<StartPageViewModel>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Entries)
            {
                Entries.Add((logLevel, formatter(state, exception) + (exception is null ? string.Empty : " " + exception)));
            }
        }
    }
}
