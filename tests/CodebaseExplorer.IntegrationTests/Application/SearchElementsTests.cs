using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Application.Search;
using CodebaseExplorer.TestRepositories;
using Microsoft.Extensions.DependencyInjection;

namespace CodebaseExplorer.IntegrationTests.Application;

/// <summary>GRAPH-007: element search (Ctrl/Cmd+K) over a stored model.</summary>
public sealed class SearchElementsTests : IAsyncLifetime
{
    private readonly TestApplication _app = new();
    private string _repositoryId = string.Empty;

    private SearchElements Search => _app.Services.GetRequiredService<SearchElements>();

    public async ValueTask InitializeAsync()
    {
        await _app.InitializeAsync();
        _repositoryId = (await _app.Services.GetRequiredService<ImportLocalRepository>().ExecuteAsync(
            TestRepositoryCatalog.GetFixturePath("ts-monorepo-workspaces"), null, TestContext.Current.CancellationToken)).Value.RepositoryId;
    }

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    [Fact]
    public async Task Exact_names_come_first_then_prefixes_then_contained_names()
    {
        var results = (await Search.ExecuteAsync(_repositoryId, "order", cancellationToken: TestContext.Current.CancellationToken)).Value;

        Assert.Equal(
            // internalHelper matches only through its qualified name (src/orders/…), so it comes last.
            ["src/orders/order.ts#Order", "src/orders/order.ts#OrderId", "src/orders/order-service.ts#OrderService", "src/orders/order-service.ts#internalHelper"],
            results.Items.Where(i => i.Element.Kind == "entity").Select(i => i.Element.Label));
        Assert.Equal("Interface", results.Items[0].KindText);
        Assert.Equal("@acme/core · src/orders/order.ts", results.Items[0].Container);
        Assert.Equal(results.Items.Count, results.TotalCount);
    }

    [Fact]
    public async Task Results_carry_the_graph_node_and_the_expansions_to_show_them()
    {
        var service = (await Search.ExecuteAsync(_repositoryId, "OrderService", cancellationToken: TestContext.Current.CancellationToken)).Value.Items[0];

        Assert.Equal("Class", service.KindText);
        Assert.Equal(service.Element.Id, service.Element.GraphNodeId); // a type is its own node
        Assert.Equal(4, service.Element.ExpandPath.Count); // project, src, src/orders, the file module
    }

    [Fact]
    public async Task Projects_folders_and_packages_are_found_case_insensitively()
    {
        var results = (await Search.ExecuteAsync(_repositoryId, "ACME", cancellationToken: TestContext.Current.CancellationToken)).Value;

        Assert.Equal(["@acme/core", "@acme/shared", "acme-monorepo"], results.Items.Where(i => i.Element.Kind == "project").Select(i => i.Element.Label).Order(StringComparer.Ordinal));
        Assert.Contains((await Search.ExecuteAsync(_repositoryId, "lodash", cancellationToken: TestContext.Current.CancellationToken)).Value.Items,
            i => i.Element.Kind == "external-dependency" && i.KindText == "Package (npm)");
    }

    [Fact]
    public async Task Limit_empty_text_and_errors()
    {
        var limited = (await Search.ExecuteAsync(_repositoryId, "s", limit: 2, cancellationToken: TestContext.Current.CancellationToken)).Value;
        Assert.Equal(2, limited.Items.Count);
        Assert.True(limited.TotalCount > 2);

        Assert.Empty((await Search.ExecuteAsync(_repositoryId, "   ", cancellationToken: TestContext.Current.CancellationToken)).Value.Items);
        Assert.Equal(SearchErrorCodes.LimitInvalid, (await Search.ExecuteAsync(_repositoryId, "a", limit: 0, cancellationToken: TestContext.Current.CancellationToken)).Error.Code);
        Assert.Equal(RepositoryErrorCodes.NotAnalyzed, (await Search.ExecuteAsync(new string('d', 32), "a", cancellationToken: TestContext.Current.CancellationToken)).Error.Code);
    }
}
