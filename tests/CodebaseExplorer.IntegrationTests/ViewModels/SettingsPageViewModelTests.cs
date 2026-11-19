using CodebaseExplorer.Application.Abstractions;
using CodebaseExplorer.Application.AI;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Application.Settings;
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
            _app.Services.GetRequiredService<AnalysisSettingsStore>(), _app.Services.GetRequiredService<PrepareEditorLaunch>(),
            _app.Services.GetRequiredService<RemoveAllRepositories>(), _app.Services.GetRequiredService<RepositorySession>(),
            _shell, NullLogger<SettingsPageViewModel>.Instance);

    [Fact]
    public async Task AnalysisSettings_AreSaved_AndInvalidFolderNamesAreRejected()
    {
        var viewModel = CreateViewModel(Substitute.For<IThemeService>());
        await viewModel.ActivateAsync();
        Assert.Equal(AnalysisSettings.Default.MaxFiles, viewModel.MaxFiles);

        viewModel.ExcludedFolders = "vendor\r\ngenerated";
        viewModel.MaxFiles = 2000;
        viewModel.HistoryDays = 0;
        await viewModel.SaveAnalysisSettingsCommand.ExecuteAsync(null);
        Assert.StartsWith("Saved.", viewModel.AnalysisStatusMessage, StringComparison.Ordinal);

        var stored = await _app.Services.GetRequiredService<AnalysisSettingsStore>().LoadAsync(Token);
        Assert.Equal(["vendor", "generated"], stored.ExcludedFolders);
        Assert.Equal((2000, 0), (stored.MaxFiles, stored.HistoryDays));

        viewModel.ExcludedFolders = "../secret";
        await viewModel.SaveAnalysisSettingsCommand.ExecuteAsync(null);
        Assert.Contains("is not a folder name", viewModel.AnalysisStatusMessage, StringComparison.Ordinal);

        await viewModel.ResetAnalysisSettingsCommand.ExecuteAsync(null);
        Assert.Equal(AnalysisSettings.Default, await _app.Services.GetRequiredService<AnalysisSettingsStore>().LoadAsync(Token)
            with { ExcludedFolders = AnalysisSettings.Default.ExcludedFolders });
    }

    [Fact]
    public async Task EditorCommand_IsValidatedAndSaved()
    {
        var viewModel = CreateViewModel(Substitute.For<IThemeService>());
        await viewModel.ActivateAsync();
        Assert.Null(viewModel.EditorCommand);

        viewModel.EditorCommand = "code --goto";
        await viewModel.SaveEditorCommandCommand.ExecuteAsync(null);
        Assert.Contains("{path}", viewModel.EditorStatusMessage, StringComparison.Ordinal);

        viewModel.EditorCommand = "code --goto \"{path}:{line}\"";
        await viewModel.SaveEditorCommandCommand.ExecuteAsync(null);
        Assert.Equal("Saved.", viewModel.EditorStatusMessage);
        Assert.Equal("code --goto \"{path}:{line}\"", await _app.Services.GetRequiredService<PrepareEditorLaunch>().GetTemplateAsync(Token));
    }

    [Fact]
    public async Task DeleteAllData_NeedsConfirmation_ThenDeletesEverything_AndClosesTheRepository()
    {
        using var copy = CodebaseExplorer.IntegrationTests.Security.ZeroExecutionHarness.CopyFixture("dotnet-layered");
        var summary = (await _app.Services.GetRequiredService<ImportLocalRepository>().ExecuteAsync(copy.FullPath, null, Token)).Value;
        var session = _app.Services.GetRequiredService<RepositorySession>();
        session.SetCurrent((await _app.Services.GetRequiredService<OpenRepository>().ExecuteAsync(summary.RepositoryId, Token)).Value);
        var viewModel = CreateViewModel(Substitute.For<IThemeService>());

        Assert.False(viewModel.DeleteAllDataCommand.CanExecute(null));
        viewModel.ConfirmDeleteAll = true;
        await viewModel.DeleteAllDataCommand.ExecuteAsync(null);

        Assert.StartsWith("Deleted 1 repositories", viewModel.DeleteAllMessage, StringComparison.Ordinal);
        Assert.Empty(await _app.Services.GetRequiredService<ListRecentRepositories>().ExecuteAsync(Token));
        Assert.Null(session.Current);
        Assert.False(viewModel.ConfirmDeleteAll);
        Assert.True(File.Exists(Path.Combine(copy.FullPath, "Shop.sln")), "The user's folder is never touched.");
    }
}
