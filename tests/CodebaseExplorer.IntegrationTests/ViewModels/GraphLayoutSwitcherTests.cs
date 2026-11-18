using CodebaseExplorer.Application.Graph;
using CodebaseExplorer.Application.Settings;
using CodebaseExplorer.Desktop.Services;
using CodebaseExplorer.Desktop.ViewModels.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CodebaseExplorer.IntegrationTests.ViewModels;

/// <summary>
/// GRAPH-005: the layout switcher of the Architecture Explorer applies the chosen layout to the graph
/// host and persists it under <see cref="UserSettingKeys.GraphLayout"/>. Substituted host and settings store.
/// </summary>
public sealed class GraphLayoutSwitcherTests : IAsyncLifetime
{
    private readonly TestApplication _app = new();
    private readonly IGraphViewHost _host = Substitute.For<IGraphViewHost>();
    private readonly IUserSettingsStore _settings = Substitute.For<IUserSettingsStore>();
    private readonly ShellStatus _shell = new(TimeProvider.System);
    private ArchitectureExplorerPageViewModel _viewModel = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await _app.InitializeAsync();
        _host.Selection.Returns(GraphSelection.Empty);
        _host.SetLayoutAsync(default, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(true);
        _viewModel = new ArchitectureExplorerPageViewModel(
            _host, _app.Services.GetRequiredService<GetGraphView>(), _app.Services.GetRequiredService<GetGraphEdgeDetails>(),
            _app.Services.GetRequiredService<CodebaseExplorer.Application.Source.GetSourceSnippet>(), Substitute.For<IClipboardService>(),
            new RepositorySession(), _settings, _shell, NullLogger<ArchitectureExplorerPageViewModel>.Instance);
    }

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    [Fact]
    public async Task OffersAllLayouts_WithForceAsDefault_WithoutSendingOrStoring()
    {
        Assert.Equal([GraphLayout.Force, GraphLayout.Hierarchical, GraphLayout.Concentric], _viewModel.Layouts.Select(o => o.Layout));
        Assert.Equal(["Force-directed", "Hierarchical", "Concentric"], _viewModel.Layouts.Select(o => o.ToString()));
        Assert.Equal(GraphLayout.Force, _viewModel.SelectedLayout.Layout);
        await _host.DidNotReceiveWithAnyArgs().SetLayoutAsync(default, Arg.Any<CancellationToken>());
        await _settings.DidNotReceiveWithAnyArgs().SetAsync(default!, default!, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("hierarchical", GraphLayout.Hierarchical)]
    [InlineData("concentric", GraphLayout.Concentric)]
    [InlineData("force", GraphLayout.Force)]
    [InlineData(null, GraphLayout.Force)]
    [InlineData("cose", GraphLayout.Force)]
    public async Task Restore_AppliesTheStoredLayout_WithoutStoringItAgain(string? stored, GraphLayout expected)
    {
        _settings.GetAsync(UserSettingKeys.GraphLayout, Arg.Any<CancellationToken>()).Returns(stored);

        await _viewModel.RestoreLayoutAsync(Token);

        Assert.Equal(expected, _viewModel.SelectedLayout.Layout);
        await _host.Received(1).SetLayoutAsync(expected, Arg.Any<CancellationToken>());
        await _settings.DidNotReceiveWithAnyArgs().SetAsync(default!, default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Restore_UnreadableSetting_UsesTheDefault()
    {
        _settings.GetAsync(UserSettingKeys.GraphLayout, Arg.Any<CancellationToken>()).ThrowsAsync(new IOException("database locked"));

        await _viewModel.RestoreLayoutAsync(Token);

        Assert.Equal(GraphLayout.Force, _viewModel.SelectedLayout.Layout);
        await _host.Received(1).SetLayoutAsync(GraphLayout.Force, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UserChange_AppliesAndStoresTheLayout()
    {
        _viewModel.SelectedLayout = _viewModel.Layouts.Single(o => o.Layout == GraphLayout.Hierarchical);
        await Task.Yield();

        await _host.Received(1).SetLayoutAsync(GraphLayout.Hierarchical, Arg.Any<CancellationToken>());
        await _settings.Received(1).SetAsync(UserSettingKeys.GraphLayout, "hierarchical", Arg.Any<CancellationToken>());
        Assert.Empty(_shell.Notifications);
    }

    [Fact]
    public async Task UserChange_SaveFails_NotifiesTheUser()
    {
        _settings.SetAsync(default!, default!, Arg.Any<CancellationToken>()).ThrowsAsyncForAnyArgs(new IOException("disk full"));

        _viewModel.SelectedLayout = _viewModel.Layouts.Single(o => o.Layout == GraphLayout.Concentric);
        await Task.Yield();

        await _host.Received(1).SetLayoutAsync(GraphLayout.Concentric, Arg.Any<CancellationToken>());
        Assert.Equal(
            "The graph layout could not be saved and will be reset at the next start.",
            Assert.Single(_shell.Notifications).Message);
    }

    [Theory]
    [InlineData(GraphLayout.Force)]
    [InlineData(GraphLayout.Hierarchical)]
    [InlineData(GraphLayout.Concentric)]
    public void Tokens_RoundTrip(GraphLayout layout) => Assert.Equal(layout, GraphLayouts.Parse(layout.ToToken()));
}
