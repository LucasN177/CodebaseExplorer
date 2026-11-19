using CodebaseExplorer.Application.Abstractions;
using CodebaseExplorer.Application.AI;
using CodebaseExplorer.Desktop.Services;
using CodebaseExplorer.Desktop.ViewModels.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CodebaseExplorer.IntegrationTests.ViewModels;

/// <summary>
/// STORE-005: the theme choice is stored and restored after a restart. Data directory: opening it in the file manager,
/// disk usage and compacting the database (real SQLite through the composition root).
/// </summary>
public sealed class SettingsPageViewModelTests : IAsyncLifetime
{
    private readonly TestApplication _app = new();
    private readonly ShellStatus _shell = new(TimeProvider.System);
    private readonly IFolderLauncher _folders = Substitute.For<IFolderLauncher>();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => _app.InitializeAsync();

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    [Fact]
    public async Task Theme_SurvivesRestart()
    {
        var theme = _app.Services.GetRequiredService<IThemeService>();
        await theme.ChangeAsync(AppTheme.Dark, Token);

        await using var restarted = _app.Restart();
        var restored = restarted.Services.GetRequiredService<IThemeService>();
        Assert.Equal(AppTheme.System, restored.CurrentTheme);

        await restored.RestoreAsync(Token);

        Assert.Equal(AppTheme.Dark, restored.CurrentTheme);
    }

    [Fact]
    public async Task NothingStored_RestoresSystemTheme()
    {
        var theme = _app.Services.GetRequiredService<IThemeService>();

        await theme.RestoreAsync(Token);

        Assert.Equal(AppTheme.System, theme.CurrentTheme);
    }

    [Fact]
    public void SelectingATheme_ChangesAndStoresIt()
    {
        var theme = Substitute.For<IThemeService>();
        theme.CurrentTheme.Returns(AppTheme.Light);
        var viewModel = CreateViewModel(theme);

        Assert.Equal(AppTheme.Light, viewModel.SelectedTheme);
        theme.DidNotReceiveWithAnyArgs().ChangeAsync(default, Arg.Any<CancellationToken>());

        viewModel.SelectedTheme = AppTheme.Dark;

        theme.Received(1).ChangeAsync(AppTheme.Dark, Arg.Any<CancellationToken>());
        Assert.Empty(_shell.Notifications);
    }

    [Fact]
    public void SavingFails_ShowsNotification()
    {
        var theme = Substitute.For<IThemeService>();
        theme.ChangeAsync(default, Arg.Any<CancellationToken>()).ThrowsAsyncForAnyArgs(new InvalidOperationException("disk full"));
        var viewModel = CreateViewModel(theme);

        viewModel.SelectedTheme = AppTheme.Dark;

        var notification = Assert.Single(_shell.Notifications);
        Assert.Equal(NotificationSeverity.Error, notification.Severity);
    }

    [Fact]
    public async Task OpenDataFolder_UsesTheLauncher_AndReportsFailures()
    {
        var viewModel = CreateViewModel(Substitute.For<IThemeService>());
        _folders.OpenAsync(default!).ReturnsForAnyArgs(true, false);

        await viewModel.OpenDataDirectoryCommand.ExecuteAsync(null);
        Assert.Empty(_shell.Notifications);
        await _folders.Received(1).OpenAsync(viewModel.DataDirectory);

        await viewModel.OpenLogsDirectoryCommand.ExecuteAsync(null);
        await _folders.Received(1).OpenAsync(viewModel.LogsDirectory);
        Assert.Equal(NotificationSeverity.Error, Assert.Single(_shell.Notifications).Severity);
    }

    [Fact]
    public async Task Activating_ShowsTheDiskUsage_AndCompactingGivesSpaceBack()
    {
        // Fill the database with a repository, then remove it: the rows are gone, the file keeps its size.
        var summary = (await _app.Services.GetRequiredService<CodebaseExplorer.Application.Repositories.ImportLocalRepository>().ExecuteAsync(
            CodebaseExplorer.TestRepositories.TestRepositoryCatalog.GetFixturePath("dotnet-advanced"), null, Token)).Value;
        await _app.Services.GetRequiredService<CodebaseExplorer.Application.Repositories.RemoveRepository>().ExecuteAsync(summary.RepositoryId, Token);
        var storage = _app.Services.GetRequiredService<IStorageMaintenance>();
        var before = storage.GetUsage().DatabaseBytes;
        var viewModel = CreateViewModel(Substitute.For<IThemeService>());

        await viewModel.ActivateAsync();
        Assert.StartsWith("Database ", viewModel.StorageUsageText, StringComparison.Ordinal);

        await viewModel.CompactCommand.ExecuteAsync(null);

        Assert.StartsWith("Compacted: ", viewModel.CompactionMessage, StringComparison.Ordinal);
        Assert.True(storage.GetUsage().DatabaseBytes < before, $"{storage.GetUsage().DatabaseBytes} >= {before}");
        Assert.Empty(await _app.Services.GetRequiredService<CodebaseExplorer.Application.Repositories.ListRecentRepositories>().ExecuteAsync(Token));
        Assert.False(_shell.IsBusy);
    }

    [Fact]
    public async Task CompactionFailure_IsShownOnThePage()
    {
        var storage = Substitute.For<IStorageMaintenance>();
        storage.GetUsage().Returns(new StorageUsage(2048, 0));
        storage.CompactAsync(Arg.Any<CancellationToken>()).Returns(CodebaseExplorer.Application.Common.ApplicationError.Failure(DatabaseErrorCodes.CompactionFailed, "busy"));
        var viewModel = CreateViewModel(Substitute.For<IThemeService>(), storage);

        await viewModel.CompactCommand.ExecuteAsync(null);

        Assert.Equal("busy", viewModel.CompactionMessage);
        Assert.Equal("Database 2.0 KB · backups 0 B", viewModel.StorageUsageText);
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(5L * 1024 * 1024, "5.0 MB")]
    [InlineData(3L * 1024 * 1024 * 1024, "3.00 GB")]
    public void Sizes_AreReadable(long bytes, string expected) => Assert.Equal(expected, SettingsPageViewModel.Size(bytes));

    [Fact]
    public async Task AiSettings_AreOffByDefault_AndSavedValuesSurviveActivation()
    {
        var viewModel = CreateViewModel(Substitute.For<IThemeService>());
        await viewModel.ActivateAsync();
        Assert.False(viewModel.AiEnabled);
        Assert.Equal(AiSettings.DefaultEndpoint, viewModel.AiEndpoint);
        Assert.Null(viewModel.AiRemoteWarning);

        viewModel.AiEnabled = true;
        viewModel.AiModel = "qwen2.5-coder:7b";
        viewModel.AiMaxContextTokens = 16384;
        await viewModel.SaveAiSettingsCommand.ExecuteAsync(null);
        Assert.Equal("Saved.", viewModel.AiStatusMessage);

        var reloaded = CreateViewModel(Substitute.For<IThemeService>());
        await reloaded.ActivateAsync();
        Assert.True(reloaded.AiEnabled);
        Assert.Equal("qwen2.5-coder:7b", reloaded.AiModel);
        Assert.Equal(16384, reloaded.AiMaxContextTokens);
    }

    [Fact]
    public async Task RemoteEndpoint_ShowsAWarning_AndInvalidValuesAreNotSaved()
    {
        var viewModel = CreateViewModel(Substitute.For<IThemeService>());
        await viewModel.ActivateAsync();
        viewModel.AiEnabled = true;

        viewModel.AiEndpoint = "http://gpu-box.example:11434";
        Assert.NotNull(viewModel.AiRemoteWarning);

        viewModel.AiEndpoint = "ftp://localhost";
        await viewModel.SaveAiSettingsCommand.ExecuteAsync(null);
        Assert.StartsWith("The endpoint must be", viewModel.AiStatusMessage, StringComparison.Ordinal);
        Assert.False((await _app.Services.GetRequiredService<AiSettingsStore>().LoadAsync(Token)).Enabled);
    }

    private SettingsPageViewModel CreateViewModel(IThemeService theme, IStorageMaintenance? storage = null) =>
        new(theme, _app.Services.GetRequiredService<IApplicationPaths>(), storage ?? _app.Services.GetRequiredService<IStorageMaintenance>(),
            _folders, _app.Services.GetRequiredService<AiSettingsStore>(), _app.Services.GetRequiredService<IAiAssistant>(),
            _shell, NullLogger<SettingsPageViewModel>.Instance);
}
