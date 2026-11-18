using CodebaseExplorer.Application.Abstractions;
using CodebaseExplorer.Desktop.Services;
using CodebaseExplorer.Desktop.ViewModels.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CodebaseExplorer.IntegrationTests.ViewModels;

/// <summary>STORE-005: the theme choice is stored and restored after a restart.</summary>
public sealed class SettingsPageViewModelTests : IAsyncLifetime
{
    private readonly TestApplication _app = new();
    private readonly ShellStatus _shell = new(TimeProvider.System);

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

    private SettingsPageViewModel CreateViewModel(IThemeService theme) =>
        new(theme, _app.Services.GetRequiredService<IApplicationPaths>(), _shell, NullLogger<SettingsPageViewModel>.Instance);
}
