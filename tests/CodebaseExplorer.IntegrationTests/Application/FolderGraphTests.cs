using CodebaseExplorer.Application.Graph;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Contracts.Graph;
using CodebaseExplorer.TestRepositories;
using Microsoft.Extensions.DependencyInjection;

namespace CodebaseExplorer.IntegrationTests.Application;

/// <summary>GRAPH-009: a repository without recognized projects shows a folder graph (fixture plain-folders, EXPECTED.md).</summary>
public sealed class FolderGraphTests : IAsyncLifetime
{
    private readonly TestApplication _app = new();

    public ValueTask InitializeAsync() => _app.InitializeAsync();

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    [Fact]
    public async Task RepositoryWithoutProjects_ShowsItsFolders()
    {
        var token = TestContext.Current.CancellationToken;
        var summary = (await _app.Services.GetRequiredService<ImportLocalRepository>().ExecuteAsync(
            TestRepositoryCatalog.GetFixturePath("plain-folders"), null, token)).Value;
        Assert.Equal(("succeeded", 0, 0, 0), (summary.Status, summary.ProjectCount, summary.RelationshipCount, summary.WarningCount));

        var graph = _app.Services.GetRequiredService<GetGraphView>();
        var query = new GraphQuery { RepositoryId = summary.RepositoryId };
        var view = (await graph.ExecuteAsync(query, token)).Value;

        Assert.Equal(GraphLevels.Folder, view.Level);
        Assert.Empty(view.Edges);
        Assert.Equal(
            ["docs 2 True", "notes 1 False", "scripts 2 False", "(repository root) 2 False"],
            view.Nodes.Select(n => $"{n.Label} {n.Metrics.Size} {n.IsExpandable}"));

        var docs = view.Nodes[0].Id;
        var expanded = (await graph.ExecuteAsync(query with { Expanded = [docs] }, token)).Value;
        Assert.Equal(["guides"], expanded.Nodes.Where(n => n.ParentId == docs).Select(n => n.Label));
    }
}
