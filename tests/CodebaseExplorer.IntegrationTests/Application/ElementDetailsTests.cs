using CodebaseExplorer.Application.Inspector;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Contracts.Inspector;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Model;
using CodebaseExplorer.TestRepositories;
using Microsoft.Extensions.DependencyInjection;

namespace CodebaseExplorer.IntegrationTests.Application;

/// <summary>UI-006: Component Inspector details over the stored model of <c>dotnet-advanced</c> (EXPECTED.md §8–§10).</summary>
public sealed class ElementDetailsTests : IAsyncLifetime
{
    private readonly TestApplication _app = new();
    private string _repositoryId = null!;
    private ArchitectureModel _model = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await _app.InitializeAsync();
        var imported = (await _app.Services.GetRequiredService<ImportLocalRepository>()
            .ExecuteAsync(TestRepositoryCatalog.GetFixturePath("dotnet-advanced"), null, Token)).Value;
        _repositoryId = imported.RepositoryId;
        _model = (await _app.Services.GetRequiredService<IArchitectureModelStore>()
            .LoadCurrentModelAsync(new RepositoryId(Guid.ParseExact(_repositoryId, "N")), Token))!.Model;
    }

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    private string EntityId(string key) => _model.Entities.Single(e => e.SymbolKey == key).Id.ToString();

    private async Task<ElementDetailsDto> DetailsAsync(string elementId) =>
        (await _app.Services.GetRequiredService<GetElementDetails>().ExecuteAsync(_repositoryId, elementId, Token)).Value;

    private static IEnumerable<(string Kind, string Label, string Level, string? Via)> Flatten(IEnumerable<RelationshipGroupDto> groups) =>
        groups.SelectMany(g => g.Items.Select(i => (g.Kind, i.Element.Label, i.Level, i.Via)));

    [Fact]
    public async Task Type_ShowsFactsMembersAndBothDirections()
    {
        var details = await DetailsAsync(EntityId("T:Advanced.Core.Entities.Customer"));

        Assert.Equal(("entity", "Customer", "Class"), (details.Kind, details.Name, details.KindText));
        Assert.Contains(new ElementFactDto("Modifiers", "Partial"), details.Facts);
        Assert.Contains(new ElementFactDto("Declared in", "src/Advanced.Core/Entities/Customer.cs:5"), details.Facts);
        Assert.Contains(new ElementFactDto("Declared in", "src/Advanced.Core/Entities/Customer.Orders.cs:3"), details.Facts);
        Assert.Contains(details.Members, m => m.Label == "Advanced.Core.Entities.Customer.AddOrder");
        Assert.Contains(details.Members, m => m.Label == "Advanced.Core.Entities.Customer.OrderSummary");

        var outgoing = Flatten(details.Outgoing).ToList();
        Assert.Contains(("inherits", "Advanced.Core.Entities.Entity<TId>", "confirmed", null), outgoing);
        Assert.Contains(("implements", "Advanced.Core.Abstractions.IAuditable", "confirmed", null), outgoing);
        // A member's relationship is listed with the member as "via"; relationships inside the type are not listed.
        Assert.Contains(outgoing, r => r.Kind == "calls" && r.Label == "Advanced.Core.Entities.Entity<TId>.Entity" && r.Via == "Advanced.Core.Entities.Customer.Customer");
        Assert.DoesNotContain(outgoing, r => r.Label.StartsWith("Advanced.Core.Entities.Customer.", StringComparison.Ordinal));

        var incoming = Flatten(details.Incoming).ToList();
        Assert.Contains(incoming, r => r.Kind == "instantiates" && r.Label == "Advanced.Services.Customers.CustomerService.Create");
        Assert.Equal(incoming.Select(r => r.Label).Distinct().Count(), details.FanIn);
        Assert.True(details.FanOut >= 2);
    }

    [Fact]
    public async Task RelatedElements_KnowWhichNodesToExpandAndFocus()
    {
        var details = await DetailsAsync(EntityId("T:Advanced.Services.Customers.CustomerService"));

        var target = details.Outgoing.SelectMany(g => g.Items).First(i => i.Element.Label == "Advanced.Core.Abstractions.IRepository<T>").Element;
        var core = _model.Projects.Single(p => p.Name == "Advanced.Core").Id.ToString();
        var modules = _model.Modules.Where(m => m.ProjectId.ToString() == core).ToDictionary(m => m.QualifiedName, m => m.Id.ToString());

        Assert.Equal(EntityId("T:Advanced.Core.Abstractions.IRepository`1"), target.GraphNodeId);
        Assert.Equal([core, modules["Advanced"], modules["Advanced.Core"], modules["Advanced.Core.Abstractions"]], target.ExpandPath);

        // A member is shown by its type.
        var call = details.Outgoing.Single(g => g.Kind == "calls").Items.First(i => i.Element.Label.EndsWith(".Add", StringComparison.Ordinal)).Element;
        Assert.Equal(EntityId("T:Advanced.Core.Abstractions.IRepository`1"), call.GraphNodeId);
    }

    [Fact]
    public async Task Project_AggregatesItsSubtree()
    {
        var services = _model.Projects.Single(p => p.Name == "Advanced.Services");

        var details = await DetailsAsync(services.Id.ToString());

        Assert.Equal(("project", "Project"), (details.Kind, details.KindText));
        Assert.Contains(new ElementFactDto("Folder", "src/Advanced.Services"), details.Facts);
        Assert.Contains(new ElementFactDto("Target framework(s)", "net10.0"), details.Facts);
        Assert.Equal(_model.Files.Count(f => f.ProjectId == services.Id), details.Size);
        var outgoing = Flatten(details.Outgoing).ToList();
        Assert.Contains(("project-reference", "Advanced.Core", "confirmed", null), outgoing);
        Assert.Contains(("project-reference", "Advanced.Plugins", "syntactic", null), outgoing);
        Assert.Contains(outgoing, r => r.Kind == "package-reference" && r.Label == "Serilog");
        Assert.Contains(outgoing, r => r.Kind == "implements" && r.Label == "Advanced.Core.Abstractions.IRepository<T>");
        Assert.DoesNotContain(outgoing, r => r.Label.StartsWith("Advanced.Services", StringComparison.Ordinal)); // internal
        Assert.Empty(details.Incoming);
    }

    [Fact]
    public async Task Module_AndExternalDependency_AreDescribed()
    {
        var abstractions = _model.Modules.Single(m => m.QualifiedName == "Advanced.Core.Abstractions");
        var serilog = _model.ExternalDependencies.Single(d => d.Name == "serilog");

        var module = await DetailsAsync(abstractions.Id.ToString());
        var package = await DetailsAsync(serilog.Id.ToString());

        Assert.Equal(("module", "Namespace", "Advanced.Core.Abstractions"), (module.Kind, module.KindText, module.Name));
        Assert.Equal(["IAuditable", "IRepository<T>", "Result<T>", "ResultHandler<T>", "ResultKind"],
            module.Members.Select(m => m.Label.Split('.')[^1]).Order(StringComparer.Ordinal));
        Assert.Contains(Flatten(module.Incoming), r => r.Kind == "implements" && r.Label == "Advanced.Core.Entities.Customer");
        Assert.Equal(("external-dependency", "Serilog"), (package.Kind, package.Name));
        Assert.Equal([("package-reference", "Advanced.Services", "confirmed", (string?)null)], Flatten(package.Incoming));
    }

    [Theory]
    [InlineData("nope", InspectorErrorCodes.ElementInvalid)]
    [InlineData("0123456789abcdef0123456789abcdef", InspectorErrorCodes.ElementNotFound)]
    public async Task UnknownOrInvalidElements_AreErrors(string elementId, string code)
    {
        var result = await _app.Services.GetRequiredService<GetElementDetails>().ExecuteAsync(_repositoryId, elementId, Token);

        Assert.Equal(code, result.Error.Code);
    }
}
