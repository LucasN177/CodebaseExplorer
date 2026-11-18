using CodebaseExplorer.Application.Graph;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Contracts.Graph;
using CodebaseExplorer.Desktop.Composition;
using CodebaseExplorer.TestRepositories;
using Microsoft.Extensions.DependencyInjection;

namespace CodebaseExplorer.IntegrationTests.Application;

/// <summary>
/// GRAPH-010: fixture <c>dotnet-layered</c> → import → evidence of every project edge, with the
/// locations listed in EXPECTED.md §3 (real composition root).
/// </summary>
public sealed class GetGraphEdgeDetailsTests : IDisposable
{
    private readonly ServiceProvider _services = CompositionRoot.BuildServiceProvider();

    public void Dispose() => _services.Dispose();

    [Fact]
    public async Task EveryEdge_HasEvidenceWithFileAndLine()
    {
        var (query, view) = await ImportAsync();
        var getDetails = _services.GetRequiredService<GetGraphEdgeDetails>();
        var labels = view.Nodes.ToDictionary(n => n.Id, n => n.Label);

        var locations = new List<(string, string, string)>();
        foreach (var edge in view.Edges)
        {
            var details = (await getDetails.ExecuteAsync(query, edge.Id, TestContext.Current.CancellationToken)).Value;
            var evidence = Assert.Single(Assert.Single(details.Relationships).Evidence);
            Assert.NotNull(evidence.StartLine);
            locations.Add((labels[edge.Source], labels[edge.Target], $"{evidence.Path}:{evidence.StartLine}"));
        }

        Assert.Contains(("Shop.Web", "Shop.Application", "src/Shop.Web/Shop.Web.csproj:13"), locations);
        Assert.Contains(("Shop.Web", "Shop.Infrastructure", "src/Shop.Web/Shop.Web.csproj:14"), locations);
        Assert.Equal(view.Edges.Count, locations.Count);
    }

    [Fact]
    public async Task UnknownEdge_IsNotFound()
    {
        var (query, _) = await ImportAsync();

        var result = await _services.GetRequiredService<GetGraphEdgeDetails>()
            .ExecuteAsync(query, "unknown|edge", TestContext.Current.CancellationToken);

        Assert.Equal(GraphErrorCodes.EdgeNotFound, result.Error.Code);
    }

    [Fact]
    public async Task NotAnalyzedRepository_IsNotFound()
    {
        var result = await _services.GetRequiredService<GetGraphEdgeDetails>()
            .ExecuteAsync(new GraphQuery { RepositoryId = new string('a', 32) }, "a|b", TestContext.Current.CancellationToken);

        Assert.Equal(RepositoryErrorCodes.NotAnalyzed, result.Error.Code);
    }

    private async Task<(GraphQuery Query, GraphViewDto View)> ImportAsync()
    {
        var summary = (await _services.GetRequiredService<ImportLocalRepository>().ExecuteAsync(
            TestRepositoryCatalog.GetFixturePath("dotnet-layered"), null, TestContext.Current.CancellationToken)).Value;
        var query = new GraphQuery { RepositoryId = summary.RepositoryId };
        var view = (await _services.GetRequiredService<GetGraphView>().ExecuteAsync(query, TestContext.Current.CancellationToken)).Value;
        return (query, view);
    }
}
