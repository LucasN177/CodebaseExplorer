using CodebaseExplorer.Contracts.Graph;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Model;
using CodebaseExplorer.Domain.Relationships;
using CodebaseExplorer.Graph.Projection;

namespace CodebaseExplorer.Graph.Tests.Projection;

/// <summary>GRAPH-011: the class level shows every type of the repository; a type expands into its methods.</summary>
public sealed class GraphClassLevelTests
{
    private readonly GraphProjector _projector = new();
    private readonly ModelBuilder _builder = new();

    private GraphViewDto Project(ArchitectureModel model, params string[] expanded) =>
        _projector.Project(model, new GraphQuery { RepositoryId = model.RepositoryId.ToString(), Level = GraphLevels.Entity, Expanded = expanded }).Value;

    /// <summary>
    /// Shop.Core: Order (Add), Invoice (Total, Round). Shop.Web: Controller (Get) — Get calls Invoice.Total and creates Order;
    /// Total calls Round (inside Invoice). Project reference Web → Core.
    /// </summary>
    private (ArchitectureModel Model, Ids Ids) Sample()
    {
        var core = _builder.Project("Shop.Core", "Order.cs");
        var web = _builder.Project("Shop.Web", "Controller.cs");
        var orders = _builder.Module(core, "Shop.Core");
        var webModule = _builder.Module(web, "Shop.Web");
        var order = _builder.Type(core, orders, "Order");
        var add = _builder.Member(core, orders, order, "Add");
        var invoice = _builder.Type(core, orders, "Invoice");
        var total = _builder.Member(core, orders, invoice, "Total");
        var round = _builder.Member(core, orders, invoice, "Round");
        var controller = _builder.Type(web, webModule, "Controller");
        var get = _builder.Member(web, webModule, controller, "Get");

        _builder.ProjectReference(web, core);
        _builder.Relate(ElementKind.Entity, get.Id, RelationshipKind.Calls, ElementKind.Entity, total.Id);
        _builder.Relate(ElementKind.Entity, get.Id, RelationshipKind.Instantiates, ElementKind.Entity, order.Id);
        _builder.Relate(ElementKind.Entity, total.Id, RelationshipKind.Calls, ElementKind.Entity, round.Id);

        return (_builder.Build(), new Ids(order.Id.ToString(), add.Id.ToString(), invoice.Id.ToString(), total.Id.ToString(),
            round.Id.ToString(), controller.Id.ToString(), get.Id.ToString()));
    }

    [Fact]
    public void ClassLevel_ShowsAllTypesOfAllProjects_WithAggregatedEdges()
    {
        var (model, ids) = Sample();

        var view = Project(model);

        Assert.Equal(GraphLevels.Entity, view.Level);
        Assert.Equal(["Controller", "Invoice", "Order"], view.Nodes.Select(n => n.Label).Order(StringComparer.Ordinal));
        Assert.All(view.Nodes, n => Assert.True(n.Kind == GraphNodeKinds.Entity && n.ParentId is null && n.IsExpandable && !n.IsExpanded));
        Assert.Equal(
            [(ids.Controller, ids.Invoice, "calls"), (ids.Controller, ids.Order, "instantiates")],
            view.Edges.Select(e => (e.Source, e.Target, e.Kinds.Keys.Single())).OrderBy(e => e.Item2, StringComparer.Ordinal)
                .OrderBy(e => e.Item3, StringComparer.Ordinal));
    }

    [Fact]
    public void ExpandingTypes_ShowsTheirMethods_AndCallsAttachToThem()
    {
        var (model, ids) = Sample();

        var view = Project(model, ids.Controller, ids.Invoice);

        var controller = view.Nodes.Single(n => n.Id == ids.Controller);
        Assert.True(controller.IsExpanded);
        Assert.Equal(1, controller.ChildCount);
        Assert.Equal(["Round", "Total"], view.Nodes.Where(n => n.ParentId == ids.Invoice).Select(n => n.Label));
        Assert.Equal("method", view.Nodes.Single(n => n.Id == ids.Get).Sublabel);

        // Get → Total (method to method), Get → Order (collapsed type), Total → Round (inside Invoice, both visible).
        Assert.Contains(view.Edges, e => e.Source == ids.Get && e.Target == ids.Total);
        Assert.Contains(view.Edges, e => e.Source == ids.Get && e.Target == ids.Order);
        Assert.Contains(view.Edges, e => e.Source == ids.Total && e.Target == ids.Round);
        Assert.DoesNotContain(view.Edges, e => e.Source == ids.Controller || e.Target == ids.Invoice);
    }

    [Fact]
    public void ProjectLevel_TypesExpandIntoMembersToo()
    {
        var (model, ids) = Sample();
        var core = model.Projects.Single(p => p.Name == "Shop.Core").Id.ToString();
        var module = model.Modules.Single(m => m.QualifiedName == "Shop.Core").Id.ToString();

        var view = _projector.Project(model, new GraphQuery { RepositoryId = model.RepositoryId.ToString(), Expanded = [core, module, ids.Invoice] }).Value;

        Assert.Equal(["Round", "Total"], view.Nodes.Where(n => n.ParentId == ids.Invoice).Select(n => n.Label));
    }

    [Fact]
    public void ClassLevel_RespectsTheNodeBudget_AndTheNeighborhood()
    {
        var (model, ids) = Sample();

        var budget = _projector.Project(model, new GraphQuery { RepositoryId = model.RepositoryId.ToString(), Level = GraphLevels.Entity, NodeBudget = 2 }).Value;
        Assert.Equal(2, budget.Nodes.Count);
        Assert.Contains(budget.Nodes, n => n.Kind == GraphNodeKinds.Aggregate);

        var neighborhood = _projector.Project(model, new GraphQuery
        {
            RepositoryId = model.RepositoryId.ToString(), Level = GraphLevels.Entity, Focus = ids.Order, Direction = GraphDirections.Incoming,
        }).Value;
        Assert.Equal([ids.Controller, ids.Order], neighborhood.Nodes.Select(n => n.Id).Order(StringComparer.Ordinal));
    }

    private sealed record Ids(string Order, string Add, string Invoice, string Total, string Round, string Controller, string Get);
}
