using CodebaseExplorer.Application.Graph;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Contracts.Graph;
using CodebaseExplorer.TestRepositories;
using Microsoft.Extensions.DependencyInjection;

namespace CodebaseExplorer.IntegrationTests.Application;

/// <summary>
/// M4: polyglot fixtures through the real composition root — import, store, project graph and drill-down into
/// TS/JS folder and file modules.
/// </summary>
public sealed class PolyglotImportTests : IAsyncLifetime
{
    private readonly TestApplication _app = new();

    public ValueTask InitializeAsync() => _app.InitializeAsync();

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    private async Task<(CodebaseExplorer.Contracts.Repositories.RepositoryImportSummaryDto Summary, GetGraphView Graph)> ImportAsync(string fixture)
    {
        var result = await _app.Services.GetRequiredService<ImportLocalRepository>().ExecuteAsync(
            TestRepositoryCatalog.GetFixturePath(fixture), null, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        return (result.Value, _app.Services.GetRequiredService<GetGraphView>());
    }

    [Fact]
    public async Task TsMonorepo_ShowsWorkspaceProjectsAndTheirImports()
    {
        var (summary, graph) = await ImportAsync("ts-monorepo-workspaces");
        Assert.Equal(("succeeded", 4, 0), (summary.Status, summary.ProjectCount, summary.ErrorCount));

        var view = (await graph.ExecuteAsync(new GraphQuery { RepositoryId = summary.RepositoryId }, TestContext.Current.CancellationToken)).Value;
        var labels = view.Nodes.ToDictionary(n => n.Id, n => n.Label);
        Assert.Equal(["@acme/core", "@acme/shared", "acme-monorepo", "web"], view.Nodes.Select(n => n.Label));
        Assert.Equal(
            [("@acme/core", "@acme/shared"), ("web", "@acme/core")],
            view.Edges.Select(e => (labels[e.Source], labels[e.Target])).Order());

        // Drill down: @acme/core → src → its folder and file modules (labels relative to the parent).
        var core = view.Nodes.Single(n => n.Label == "@acme/core");
        var src = (await graph.ExecuteAsync(new GraphQuery { RepositoryId = summary.RepositoryId, Expanded = [core.Id] }, TestContext.Current.CancellationToken))
            .Value.Nodes.Single(n => n.ParentId == core.Id);
        Assert.Equal("src", src.Label);
        var expanded = (await graph.ExecuteAsync(new GraphQuery { RepositoryId = summary.RepositoryId, Expanded = [core.Id, src.Id] }, TestContext.Current.CancellationToken)).Value;
        Assert.Equal(["index.ts", "orders"], expanded.Nodes.Where(n => n.ParentId == src.Id).Select(n => n.Label));
    }

    [Fact]
    public async Task MixedPolyglot_HasOneProjectPerEcosystem()
    {
        var (summary, graph) = await ImportAsync("mixed-polyglot");

        var view = (await graph.ExecuteAsync(new GraphQuery { RepositoryId = summary.RepositoryId }, TestContext.Current.CancellationToken)).Value;
        Assert.Equal(
            [("Api", "dotnet", "csharp"), ("frontend", "node", "typescript"), ("report-tool", "python", "python")],
            view.Nodes.Select(n => (n.Label, n.Ecosystem, n.Language)));
        Assert.Empty(view.Edges);
        Assert.Equal((0, 0), (summary.WarningCount, summary.ErrorCount));
    }
}
