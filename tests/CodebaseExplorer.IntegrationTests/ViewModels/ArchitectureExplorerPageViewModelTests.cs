using System.ComponentModel;
using CodebaseExplorer.Application.Graph;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Application.Settings;
using CodebaseExplorer.Contracts.Graph;
using CodebaseExplorer.Contracts.Repositories;
using CodebaseExplorer.Desktop.Services;
using CodebaseExplorer.Desktop.ViewModels.Pages;
using CodebaseExplorer.TestRepositories;
using CodebaseExplorer.TestRepositories.Git;
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
    private readonly IClipboardService _clipboard = Substitute.For<IClipboardService>();
    private readonly ArchitectureExplorerPageViewModel _viewModel;

    public ArchitectureExplorerPageViewModelTests()
    {
        _host.ShowGraphAsync(default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(true);
        _host.Selection.Returns(GraphSelection.Empty);
        _viewModel = new ArchitectureExplorerPageViewModel(
            _host, _app.Services.GetRequiredService<GetGraphView>(), _app.Services.GetRequiredService<GetGraphEdgeDetails>(),
            _app.Services.GetRequiredService<CodebaseExplorer.Application.Source.GetSourceSnippet>(),
            _app.Services.GetRequiredService<CodebaseExplorer.Application.Inspector.GetElementDetails>(),
            _app.Services.GetRequiredService<CodebaseExplorer.Application.Search.SearchElements>(), _clipboard,
            _session, Substitute.For<IUserSettingsStore>(), _shell, NullLogger<ArchitectureExplorerPageViewModel>.Instance);
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
            ["Shop.Application", "Shop.Domain", "Shop.Infrastructure", "Shop.Legacy", "Shop.Web"],
            view.Nodes.Select(n => n.Label)); // the test project is hidden by default (GRAPH-007)
        Assert.Equal(6, view.Edges.Count);
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
    public async Task SelectedEvidence_ShowsTheSourceSnippet_AndCopiesThePath()
    {
        // UI-007
        var view = await ImportFixtureAsync();
        var labels = view.Nodes.ToDictionary(n => n.Id, n => n.Label);
        var edge = view.Edges.Single(e => labels[e.Source] == "Shop.Web" && labels[e.Target] == "Shop.Application");
        Select(view, [], [edge.Id]);

        _viewModel.SelectedEvidence = Assert.Single(_viewModel.SelectionEvidence);
        await WaitUntilAsync(() => _viewModel.HasSnippet); // the file is read asynchronously

        Assert.True(_viewModel.HasSnippet);
        Assert.Equal("src/Shop.Web/Shop.Web.csproj", _viewModel.SnippetPath);
        Assert.Null(_viewModel.SnippetMessage);
        Assert.Equal([13], _viewModel.SnippetLines.Where(l => l.IsEvidence).Select(l => l.Number));
        Assert.Equal(Enumerable.Range(8, 10), _viewModel.SnippetLines.Select(l => l.Number)); // 13 ± 5, file has 17 lines
        Assert.Contains("Shop.Application.csproj", _viewModel.SnippetLines.Single(l => l.IsEvidence).Text, StringComparison.Ordinal);

        Assert.True(_viewModel.CopyPathCommand.CanExecute(null));
        await _viewModel.CopyPathCommand.ExecuteAsync(null);
        await _clipboard.Received(1).SetTextAsync(Arg.Is<string>(p => p.EndsWith(Path.Combine("src", "Shop.Web", "Shop.Web.csproj"), StringComparison.Ordinal)));

        Select(view, [view.Nodes[0].Id], []);
        Assert.Null(_viewModel.SelectedEvidence);
        Assert.False(_viewModel.HasSnippet);
        Assert.Null(_viewModel.SnippetPath);
    }

    [Fact]
    public async Task NodeSelection_ShowsTheInspector_AndShowInGraphExpandsAndFocuses()
    {
        // UI-006 over dotnet-layered: Shop.Infrastructure implements Shop.Domain's IOrderRepository.
        var view = await ImportFixtureAsync();
        _host.PatchGraphAsync(default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(true);
        _host.FocusAsync(default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(true);
        var infrastructure = view.Nodes.Single(n => n.Label == "Shop.Infrastructure");

        Select(view, [infrastructure.Id], []);
        await WaitUntilAsync(() => _viewModel.HasDetails);

        Assert.Null(_viewModel.InspectorMessage);
        Assert.StartsWith("fan-in ", _viewModel.InspectorMetrics, StringComparison.Ordinal);
        Assert.Contains(_viewModel.InspectorFacts, f => f is { Label: "Folder", Value: "src/Shop.Infrastructure" });
        Assert.True(_viewModel.HasOutgoing);
        var implements = Assert.Single(_viewModel.OutgoingGroups, g => g.Title.StartsWith("implements", StringComparison.Ordinal));
        var target = Assert.Single(implements.Items);
        Assert.Equal("Shop.Domain.Orders.IOrderRepository", target.Label);
        Assert.StartsWith("confirmed · 1 occurrence · via Shop.Infrastructure.Persistence.SqliteOrderRepository", target.Detail, StringComparison.Ordinal);

        await _viewModel.ShowInGraphCommand.ExecuteAsync(target);

        // The Domain project and its namespaces were expanded so that the type is visible, then it was focused.
        var patched = LastPatch();
        Assert.Contains(patched.Nodes, n => n.Id == target.Reference.GraphNodeId);
        await _host.Received(1).FocusAsync(target.Reference.GraphNodeId!, Arg.Any<CancellationToken>());
        Assert.Null(_viewModel.InspectorMessage);

        // Selecting an edge replaces the inspector by the evidence.
        Select(view, [], [view.Edges[0].Id]);
        Assert.False(_viewModel.HasDetails);
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

    [Fact]
    public async Task ProjectsButton_CollapsesEverything_AndBackReturns()
    {
        var initial = await ImportFixtureAsync();
        _host.PatchGraphAsync(default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(true);
        var domain = initial.Nodes.Single(n => n.Label == "Shop.Domain");
        Assert.True(_viewModel.IsAtTopLevel);
        Assert.False(_viewModel.ShowTopLevelCommand.CanExecute(null));

        await _viewModel.SetExpandedAsync(domain.Id, expanded: true, TestContext.Current.CancellationToken);
        var module = Assert.Single(LastPatch().Nodes, n => n.ParentId == domain.Id);
        await _viewModel.SetExpandedAsync(module.Id, expanded: true, TestContext.Current.CancellationToken);
        Assert.False(_viewModel.IsAtTopLevel);

        await _viewModel.ShowTopLevelCommand.ExecuteAsync(null);

        Assert.True(_viewModel.IsAtTopLevel);
        Assert.Empty(_viewModel.CurrentQuery!.Expanded);
        Assert.Null(_viewModel.CurrentQuery!.Focus);
        Assert.Equal(GraphLevels.Project, _viewModel.CurrentQuery!.Level);
        Assert.Null(_viewModel.ErrorMessage);

        await _viewModel.BackCommand.ExecuteAsync(null);
        Assert.Equal(2, _viewModel.CurrentQuery!.Expanded.Count);
        Assert.True(_viewModel.ShowTopLevelCommand.CanExecute(null));
    }

    [Fact]
    public async Task ExpandAndCollapse_PatchTheGraph_AndAreReversible()
    {
        // GRAPH-006, end to end over the real model of dotnet-layered (projects → namespaces → types).
        var initial = await ImportFixtureAsync();
        _host.PatchGraphAsync(default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(true);
        var domain = initial.Nodes.Single(n => n.Label == "Shop.Domain");
        Assert.True(domain.IsExpandable);

        _host.ExpandRequested += Raise.EventWith(_host, new GraphElementEventArgs(domain.Id));
        var expanded = LastPatch();

        var module = Assert.Single(expanded.Nodes, n => n.ParentId == domain.Id);
        Assert.Equal((GraphNodeKinds.Module, "Shop.Domain.Orders"), (module.Kind, module.Label)); // "Shop" and "Shop.Domain" compressed
        Assert.True(expanded.Nodes.Single(n => n.Id == domain.Id).IsExpanded);

        await _viewModel.SetExpandedAsync(module.Id, expanded: true, TestContext.Current.CancellationToken);
        Assert.Contains(LastPatch().Nodes, n => n.ParentId == module.Id && n.Kind == GraphNodeKinds.Entity && n.Label == "Order");

        _host.CollapseRequested += Raise.EventWith(_host, new GraphElementEventArgs(domain.Id));
        var collapsed = LastPatch();
        Assert.Equal(
            System.Text.Json.JsonSerializer.Serialize(initial, GraphJsonContext.Default.GraphViewDto),
            System.Text.Json.JsonSerializer.Serialize(collapsed, GraphJsonContext.Default.GraphViewDto));
        Assert.Null(_viewModel.ErrorMessage);
    }

    [Fact]
    public async Task ActivityToggle_PatchesFileChangesIn_AndOutAgain()
    {
        // GIT-005: a Git repository (builder) with one commit of the fixture: every project counts one change per file.
        using var built = FixtureInGit();
        var initial = await ImportFixtureAsync(built.WorkingDirectory);
        _host.PatchGraphAsync(default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(true);
        Assert.All(initial.Nodes, n => Assert.Null(n.Metrics.ChangeCount));

        _viewModel.ShowActivity = true;
        await WaitUntilAsync(() => _host.ReceivedCalls().Any(c => c.GetMethodInfo().Name == nameof(IGraphViewHost.PatchGraphAsync)));
        var withActivity = LastPatch();

        Assert.Equal(2, withActivity.Nodes.Single(n => n.Label == "Shop.Web").Metrics.ChangeCount); // Program.cs + Shop.Web.csproj
        Assert.All(withActivity.Nodes, n => Assert.NotNull(n.Metrics.ChangeCount));
        Assert.Null(_viewModel.ActivityHint);

        _viewModel.ShowActivity = false;
        await WaitUntilAsync(() => _host.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IGraphViewHost.PatchGraphAsync)) == 2);
        Assert.All(LastPatch().Nodes, n => Assert.Null(n.Metrics.ChangeCount));
    }

    [Fact]
    public async Task ActivityToggle_WithoutGitData_ShowsAHint()
    {
        using var copy = Security.ZeroExecutionHarness.CopyFixture("dotnet-layered");
        await ImportFixtureAsync(copy.FullPath);
        _host.PatchGraphAsync(default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(true);

        _viewModel.ShowActivity = true;
        await WaitUntilAsync(() => _viewModel.ActivityHint is not null);

        Assert.StartsWith("No Git activity", _viewModel.ActivityHint, StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeActivity_SummarizesTheNumbersAndTheWindow()
    {
        var text = ArchitectureExplorerPageViewModel.DescribeActivity(new Contracts.Inspector.ElementActivityDto
        {
            FileChanges = 12,
            ChangedFiles = 3,
            LinesAdded = 120,
            LinesDeleted = 40,
            LastChangeAt = new DateTimeOffset(2026, 3, 1, 23, 30, 0, TimeSpan.FromHours(-2)),
            CommitsScanned = 412,
            WindowStart = new DateTimeOffset(2025, 11, 18, 0, 0, 0, TimeSpan.Zero),
            HeadCommitSha = "0123456789abcdef0123456789abcdef01234567",
            Branch = "main",
        });

        Assert.Equal(
            "12 file changes in 3 files · +120 −40 lines · last change 2026-03-02 · window: 412 commits since 2025-11-18 on main @ 0123456",
            text);
    }

    private static GitTestRepository FixtureInGit()
    {
        var builder = GitRepositoryBuilder.Create();
        var source = TestRepositoryCatalog.GetFixturePath("dotnet-layered");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            builder.Write(Path.GetRelativePath(source, file).Replace('\\', '/'), File.ReadAllText(file));
        }

        return builder.Commit("import").Build();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var waited = 0; !condition() && waited < 5000; waited += 20)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }

    private GraphViewDto LastPatch() =>
        (GraphViewDto)_host.ReceivedCalls().Last(c => c.GetMethodInfo().Name == nameof(IGraphViewHost.PatchGraphAsync)).GetArguments()[0]!;

    private async Task<GraphViewDto> ImportFixtureAsync(string? path = null)
    {
        var summary = (await _app.Services.GetRequiredService<ImportLocalRepository>().ExecuteAsync(
            path ?? TestRepositoryCatalog.GetFixturePath("dotnet-layered"), null, TestContext.Current.CancellationToken)).Value;

        // GetGraphView and the substituted host complete synchronously, so the load triggered by the session
        // has finished when SetCurrent returns; awaiting the explicit load would only repeat it.
        _session.SetCurrent(new OpenRepositoryDto
        {
            RepositoryId = summary.RepositoryId,
            Name = summary.Name,
            RootPath = summary.RootPath,
            AnalysisRunId = summary.AnalysisRunId,
        });

        var call = Assert.Single(_host.ReceivedCalls(), c => c.GetMethodInfo().Name == nameof(IGraphViewHost.ShowGraphAsync));
        return (GraphViewDto)call.GetArguments()[0]!;
    }

    // ---- GRAPH-007: filters, search, neighborhood ----

    private GraphViewDto LastShown() =>
        (GraphViewDto)_host.ReceivedCalls().Last(c => c.GetMethodInfo().Name == nameof(IGraphViewHost.ShowGraphAsync)).GetArguments()[0]!;

    [Fact]
    public async Task Filters_OfferTheRepositoryValues_AndTogglingTestsReloadsTheGraph()
    {
        await ImportFixtureAsync();

        Assert.Equal(["csharp"], _viewModel.LanguageFilters.Select(o => o.Token));
        Assert.Equal([".NET"], _viewModel.EcosystemFilters.Select(o => o.Display));
        Assert.Null(_viewModel.FilterSummary);

        _viewModel.IncludeTests = true;
        await WaitUntilAsync(() => LastShown().Nodes.Any(n => n.Label == "Shop.Tests"));
        Assert.Equal("1 filter setting changed", _viewModel.FilterSummary);

        _viewModel.RelationshipKindFilters.Single(o => o.Token == "package-reference").IsChecked = true;
        _viewModel.IncludeExternalDependencies = true;
        await WaitUntilAsync(() => LastShown().Edges.All(e => e.Kinds.ContainsKey("package-reference")) && LastShown().Nodes.Any(n => n.Kind == GraphNodeKinds.ExternalDependency));

        await _viewModel.ResetFiltersCommand.ExecuteAsync(null);
        Assert.DoesNotContain(LastShown().Nodes, n => n.Label == "Shop.Tests");
        Assert.Null(_viewModel.FilterSummary);
    }

    [Fact]
    public async Task Search_ListsMatches_AndOpeningOneShowsItsNeighborhood()
    {
        await ImportFixtureAsync();
        _host.FocusAsync(default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(true);

        _viewModel.SearchText = "PlaceOrderHandler";
        await WaitUntilAsync(() => _viewModel.SearchResults.Count > 0);

        var handler = _viewModel.SearchResults[0];
        Assert.Equal(("Shop.Application.Orders.PlaceOrderHandler", "Class"), (handler.Label, handler.KindText));
        Assert.Equal($"{_viewModel.SearchResults.Count} results", _viewModel.SearchMessage); // the class first, then e.g. its members and tests

        await _viewModel.OpenSearchResultCommand.ExecuteAsync(handler);

        var view = LastShown();
        Assert.True(_viewModel.IsNeighborhood);
        Assert.Contains(view.Nodes, n => n.Id == handler.Reference.GraphNodeId);
        Assert.Equal("Repository", _viewModel.Breadcrumbs[0].Label);
        Assert.Equal("PlaceOrderHandler", _viewModel.Breadcrumbs[^1].Label);
        await _host.Received(1).FocusAsync(handler.Reference.GraphNodeId!, Arg.Any<CancellationToken>());
        Assert.Null(_viewModel.SearchText);
        Assert.Empty(_viewModel.SearchResults);

        // Depth 2 reaches further; the repository breadcrumb returns to the whole graph.
        var neighbors = view.Nodes.Count;
        _viewModel.NeighborhoodDepth = 2;
        await WaitUntilAsync(() => LastShown().Nodes.Count > neighbors);
        await _viewModel.OpenBreadcrumbCommand.ExecuteAsync(_viewModel.Breadcrumbs[0]);
        Assert.False(_viewModel.IsNeighborhood);
    }

    [Fact]
    public async Task Neighborhood_OfTheSelectedProject_AndBackToTheWholeGraph()
    {
        var view = await ImportFixtureAsync();
        var domain = view.Nodes.Single(n => n.Label == "Shop.Domain");
        Select(view, [domain.Id], []);
        _viewModel.SelectedDirection = _viewModel.DirectionOptions.Single(o => o.Token == GraphDirections.Incoming);

        await _viewModel.ShowNeighborhoodCommand.ExecuteAsync(null);

        Assert.Equal(["Shop.Application", "Shop.Domain", "Shop.Infrastructure"], LastShown().Nodes.Select(n => n.Label));
        Assert.Equal(["Repository", "Shop.Domain"], _viewModel.Breadcrumbs.Select(b => b.Label));

        await _viewModel.ShowWholeGraphCommand.ExecuteAsync(null);
        Assert.False(_viewModel.IsNeighborhood);
        Assert.Equal(5, LastShown().Nodes.Count);
    }

    // ---- GRAPH-009: Back and the grouped view ----

    [Fact]
    public async Task Back_RestoresExpansionFiltersAndNeighborhoodStepByStep()
    {
        var view = await ImportFixtureAsync();
        _host.PatchGraphAsync(default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(true);
        Assert.False(_viewModel.CanGoBack);
        var domain = view.Nodes.Single(n => n.Label == "Shop.Domain");

        await _viewModel.SetExpandedAsync(domain.Id, expanded: true, TestContext.Current.CancellationToken);
        _viewModel.IncludeTests = true;
        await WaitUntilAsync(() => LastShown().Nodes.Any(n => n.Label == "Shop.Tests"));
        Select(LastShown(), [domain.Id], []);
        await _viewModel.ShowNeighborhoodCommand.ExecuteAsync(null);
        Assert.True(_viewModel.IsNeighborhood);
        Assert.True(_viewModel.CanGoBack);

        // 1: leaves the neighborhood, tests still shown, Shop.Domain still expanded.
        await _viewModel.BackCommand.ExecuteAsync(null);
        Assert.False(_viewModel.IsNeighborhood);
        Assert.True(_viewModel.IncludeTests);
        Assert.Contains(LastShown().Nodes, n => n.Label == "Shop.Tests");
        Assert.True(LastShown().Nodes.Single(n => n.Id == domain.Id).IsExpanded);

        // 2: the test filter is restored to hidden, the expansion stays.
        await _viewModel.BackCommand.ExecuteAsync(null);
        Assert.False(_viewModel.IncludeTests);
        Assert.DoesNotContain(LastShown().Nodes, n => n.Label == "Shop.Tests");
        Assert.True(LastShown().Nodes.Single(n => n.Id == domain.Id).IsExpanded);

        // 3: the initial view, collapsed; nothing earlier.
        await _viewModel.BackCommand.ExecuteAsync(null);
        Assert.False(LastShown().Nodes.Single(n => n.Id == domain.Id).IsExpanded);
        Assert.False(_viewModel.CanGoBack);
    }

    [Fact]
    public async Task GroupedView_ShowsTheSolutionGroup_AndGroupsAreNotInspected()
    {
        await ImportFixtureAsync();

        _viewModel.SelectedViewMode = _viewModel.ViewModes.Single(m => m.Token == GraphLevels.Repository);
        await WaitUntilAsync(() => LastShown().Level == GraphLevels.Repository);

        // The solution's projects (Shop.sln comes before Shop.slnx) and the placeholder Shop.Legacy, which no solution lists.
        Assert.Equal([".NET projects", "Shop.sln"], LastShown().Nodes.Select(n => n.Label));
        var group = LastShown().Nodes.Single(n => n.Label == "Shop.sln");
        Assert.Equal((GraphNodeKinds.Group, "4 projects"), (group.Kind, group.Sublabel)); // Shop.Tests is hidden by default
        Select(LastShown(), [group.Id], []);
        Assert.Equal("Group of projects", _viewModel.SelectionKind);
        Assert.False(_viewModel.HasDetails);
        Assert.Null(_viewModel.InspectorMessage);

        await _viewModel.BackCommand.ExecuteAsync(null);
        Assert.Equal(GraphLevels.Project, LastShown().Level);
        Assert.Equal(GraphLevels.Project, _viewModel.SelectedViewMode.Token);
    }

    private void Select(GraphViewDto view, IReadOnlyList<string> nodeIds, IReadOnlyList<string> edgeIds)
    {
        _host.CurrentGraph.Returns(view);
        _host.Selection.Returns(new GraphSelection(nodeIds, edgeIds));
        _host.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(_host, new PropertyChangedEventArgs(nameof(IGraphViewHost.Selection)));
    }

    private static OpenRepositoryDto Summary(string repositoryId) => new()
    {
        RepositoryId = repositoryId,
        Name = "unknown",
        RootPath = Path.GetTempPath(),
        AnalysisRunId = new string('d', 32),
    };
}
