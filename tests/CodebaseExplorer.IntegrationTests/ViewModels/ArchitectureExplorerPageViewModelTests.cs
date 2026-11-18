using System.ComponentModel;
using CodebaseExplorer.Application.Graph;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Contracts.Graph;
using CodebaseExplorer.Contracts.Repositories;
using CodebaseExplorer.Desktop.Services;
using CodebaseExplorer.Desktop.ViewModels.Pages;
using CodebaseExplorer.TestRepositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace CodebaseExplorer.IntegrationTests.ViewModels;

/// <summary>
/// UI-003: the explorer loads the project graph of the current repository into the graph host and
/// shows name and kind of the selection. Real use cases, substituted graph host (no WebView).
/// </summary>
public sealed class ArchitectureExplorerPageViewModelTests : IAsyncLifetime
{
    private readonly TestApplication _app = new();
    private readonly IGraphViewHost _host = Substitute.For<IGraphViewHost>();
    private readonly RepositorySession _session = new();
    private readonly ShellStatus _shell = new(TimeProvider.System);
    private readonly ArchitectureExplorerPageViewModel _viewModel;

    public ArchitectureExplorerPageViewModelTests()
    {
        _host.ShowGraphAsync(default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(true);
        _host.Selection.Returns(GraphSelection.Empty);
        _viewModel = new ArchitectureExplorerPageViewModel(
            _host, _app.Services.GetRequiredService<GetGraphView>(), _app.Services.GetRequiredService<GetGraphEdgeDetails>(),
            _session, _shell, NullLogger<ArchitectureExplorerPageViewModel>.Instance);
    }

    public ValueTask InitializeAsync() => _app.InitializeAsync();

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    [Fact]
    public void WithoutRepository_ShowsEmptyState()
    {
        Assert.False(_viewModel.HasGraph);
        Assert.True(_viewModel.ShowEmptyState);
        Assert.Null(_viewModel.RepositoryName);
        _host.DidNotReceiveWithAnyArgs().ShowGraphAsync(default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Import_LoadsTheProjectGraphIntoTheHost()
    {
        var view = await ImportFixtureAsync();

        Assert.True(_viewModel.HasGraph);
        Assert.False(_viewModel.IsLoading);
        Assert.Null(_viewModel.ErrorMessage);
        Assert.Equal("dotnet-layered", _viewModel.RepositoryName);
        Assert.Equal(GraphLevels.Project, view.Level);
        Assert.Equal(
            ["Shop.Application", "Shop.Domain", "Shop.Infrastructure", "Shop.Legacy", "Shop.Tests", "Shop.Web"],
            view.Nodes.Select(n => n.Label));
        Assert.Equal(8, view.Edges.Count);
    }

    [Fact]
    public async Task UnknownRepository_ShowsErrorInsteadOfGraph()
    {
        _session.SetCurrent(Summary(new string('c', 32)));
        await Task.Yield();

        Assert.False(_viewModel.HasGraph);
        Assert.Equal("The repository has not been analyzed yet.", _viewModel.ErrorMessage);
        await _host.DidNotReceiveWithAnyArgs().ShowGraphAsync(default!, Arg.Any<CancellationToken>());
        Assert.Equal(
            "The graph of unknown could not be shown: The repository has not been analyzed yet.",
            Assert.Single(_shell.Notifications).Message);
        Assert.False(_shell.IsBusy);
    }

    [Fact]
    public async Task HostRejectsGraph_ShowsError()
    {
        _host.ShowGraphAsync(default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(false);

        await ImportFixtureAsync();

        Assert.True(_viewModel.HasGraph);
        Assert.Equal("The graph view did not accept the graph. Details are in the application log.", _viewModel.ErrorMessage);
    }

    [Fact]
    public async Task NodeSelection_ShowsNameAndKind()
    {
        var view = await ImportFixtureAsync();
        var web = view.Nodes.Single(n => n.Label == "Shop.Web");

        Select(view, [web.Id], []);

        Assert.True(_viewModel.HasSelection);
        Assert.Equal("Shop.Web", _viewModel.SelectionName);
        Assert.Equal("Project", _viewModel.SelectionKind);
        Assert.Equal(web.Sublabel, _viewModel.SelectionDetail);
        Assert.Null(_viewModel.SelectionCountText);
    }

    [Fact]
    public async Task MissingProjectSelection_SaysTheProjectFileIsMissing()
    {
        var view = await ImportFixtureAsync();

        Select(view, [view.Nodes.Single(n => n.Label == "Shop.Legacy").Id], []);

        Assert.Equal("Project (project file missing)", _viewModel.SelectionKind);
    }

    [Fact]
    public async Task EdgeSelection_ShowsEndpointsAndRelationshipCount()
    {
        var view = await ImportFixtureAsync();
        var labels = view.Nodes.ToDictionary(n => n.Id, n => n.Label);
        var edge = view.Edges.Single(e => labels[e.Source] == "Shop.Web" && labels[e.Target] == "Shop.Application");

        Select(view, [], [edge.Id]);

        Assert.Equal("Shop.Web → Shop.Application", _viewModel.SelectionName);
        Assert.Equal("Dependency", _viewModel.SelectionKind);
        Assert.Equal("1 relationship", _viewModel.SelectionDetail);
    }

    [Fact]
    public async Task EdgeSelection_ShowsEvidenceWithFileAndLine()
    {
        // M1 acceptance: edges carry evidence (file + line) visible in the side panel (GRAPH-010).
        var view = await ImportFixtureAsync();
        var labels = view.Nodes.ToDictionary(n => n.Id, n => n.Label);
        var edge = view.Edges.Single(e => labels[e.Source] == "Shop.Web" && labels[e.Target] == "Shop.Application");

        Select(view, [], [edge.Id]);

        Assert.True(_viewModel.HasEvidence);
        Assert.Null(_viewModel.EvidenceMessage);
        var evidence = Assert.Single(_viewModel.SelectionEvidence);
        Assert.Equal("project-reference · confirmed", evidence.Title);
        Assert.Equal("src/Shop.Web/Shop.Web.csproj:13", evidence.Location); // EXPECTED.md §3
        Assert.Equal("dotnet.msbuild-static · csproj.ProjectReference", evidence.Source);
    }

    [Fact]
    public async Task EdgeToMissingProject_ShowsSyntacticEvidence()
    {
        var view = await ImportFixtureAsync();
        var labels = view.Nodes.ToDictionary(n => n.Id, n => n.Label);
        var edge = view.Edges.Single(e => labels[e.Target] == "Shop.Legacy");

        Select(view, [], [edge.Id]);

        var evidence = Assert.Single(_viewModel.SelectionEvidence);
        Assert.Equal("project-reference · syntactic", evidence.Title);
        Assert.StartsWith("src/Shop.Infrastructure/Shop.Infrastructure.csproj:", evidence.Location, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NodeSelectionAfterEdge_ClearsEvidence()
    {
        var view = await ImportFixtureAsync();
        Select(view, [], [view.Edges[0].Id]);
        Assert.True(_viewModel.HasEvidence);

        Select(view, [view.Nodes[0].Id], []);

        Assert.False(_viewModel.HasEvidence);
        Assert.Empty(_viewModel.SelectionEvidence);
        Assert.Null(_viewModel.EvidenceMessage);
    }

    [Fact]
    public async Task MultipleSelection_ShowsFirstNodeAndCount_AndClearingResetsThePanel()
    {
        var view = await ImportFixtureAsync();

        Select(view, [view.Nodes[0].Id, view.Nodes[1].Id], [view.Edges[0].Id]);
        Assert.Equal(view.Nodes[0].Label, _viewModel.SelectionName);
        Assert.Equal("3 elements selected", _viewModel.SelectionCountText);

        Select(view, [], []);
        Assert.False(_viewModel.HasSelection);
        Assert.Null(_viewModel.SelectionKind);
        Assert.Null(_viewModel.SelectionCountText);
    }

    private async Task<GraphViewDto> ImportFixtureAsync()
    {
        var summary = (await _app.Services.GetRequiredService<ImportLocalRepository>().ExecuteAsync(
            TestRepositoryCatalog.GetFixturePath("dotnet-layered"), null, TestContext.Current.CancellationToken)).Value;

        // GetGraphView and the substituted host complete synchronously, so the load triggered by the session
        // has finished when SetCurrent returns; awaiting the explicit load would only repeat it.
        _session.SetCurrent(summary);

        var call = Assert.Single(_host.ReceivedCalls(), c => c.GetMethodInfo().Name == nameof(IGraphViewHost.ShowGraphAsync));
        return (GraphViewDto)call.GetArguments()[0]!;
    }

    private void Select(GraphViewDto view, IReadOnlyList<string> nodeIds, IReadOnlyList<string> edgeIds)
    {
        _host.CurrentGraph.Returns(view);
        _host.Selection.Returns(new GraphSelection(nodeIds, edgeIds));
        _host.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(_host, new PropertyChangedEventArgs(nameof(IGraphViewHost.Selection)));
    }

    private static RepositoryImportSummaryDto Summary(string repositoryId) => new()
    {
        RepositoryId = repositoryId,
        Name = "unknown",
        RootPath = Path.GetTempPath(),
        AnalysisRunId = new string('d', 32),
        Status = "succeeded",
    };
}
