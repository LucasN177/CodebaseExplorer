using CodebaseExplorer.Application.Inspector;
using CodebaseExplorer.Contracts.Graph;
using CodebaseExplorer.IntegrationTests.AI;
using Microsoft.Extensions.DependencyInjection;

namespace CodebaseExplorer.IntegrationTests.Application;

/// <summary>GRAPH-011 on the real dotnet-layered analysis: methods of a class with their calls, and the class graph.</summary>
public sealed class MemberCallsTests(ImportedLayeredRepository repository) : IClassFixture<ImportedLayeredRepository>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task InspectorOfAClass_ListsItsMethodsWithTheirCalls()
    {
        var handler = repository.Stored.Model.Entities.Single(e => e.Name == "PlaceOrderHandler" && e.ContainingEntityId is null);

        var details = (await repository.Services.GetRequiredService<GetElementDetails>().ExecuteAsync(
            repository.Stored.Repository.Id.ToString(), handler.Id.ToString(), Token)).Value;

        var handle = details.MemberCalls.Single(m => m.Member.Label.EndsWith(".HandleAsync", StringComparison.Ordinal));
        Assert.Equal("method", handle.Kind);
        Assert.Contains(handle.Calls, c => c.Via == "calls" && c.Element.Label.EndsWith("IOrderRepository.AddAsync", StringComparison.Ordinal));
        Assert.Contains(handle.Calls, c => c.Via == "instantiates" && c.Element.Label == "Shop.Domain.Orders.Order");
        Assert.Equal("method", details.MemberCalls[0].Kind); // methods first
    }

    [Fact]
    public async Task ClassGraph_ShowsTheTypesOfTheRepository_WithoutTests()
    {
        var view = (await repository.Services.GetRequiredService<CodebaseExplorer.Application.Graph.GetGraphView>().ExecuteAsync(
            new GraphQuery { RepositoryId = repository.Stored.Repository.Id.ToString(), Level = GraphLevels.Entity }, Token)).Value;

        Assert.Contains(view.Nodes, n => n.Label == "PlaceOrderHandler" && n.IsExpandable);
        Assert.Contains(view.Nodes, n => n.Label == "SqliteOrderRepository");
        Assert.DoesNotContain(view.Nodes, n => n.Label == "PlaceOrderHandlerTests"); // test code hidden by default
        Assert.All(view.Nodes, n => Assert.Equal(GraphNodeKinds.Entity, n.Kind));
    }
}
