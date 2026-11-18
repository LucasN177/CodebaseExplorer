using CodebaseExplorer.Contracts.Graph;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Model;
using CodebaseExplorer.Domain.Relationships;
using CodebaseExplorer.Graph.Projection;

namespace CodebaseExplorer.Graph.Tests.Projection;

/// <summary>GRAPH-006: module and entity levels with expand/collapse (mixed levels, deepest-visible-ancestor edges).</summary>
public sealed class GraphExpansionTests
{
    private readonly GraphProjector _projector = new();
    private readonly ModelBuilder _builder = new();

    private GraphViewDto Project(ArchitectureModel model, params string[] expanded) =>
        _projector.Project(model, new GraphQuery { RepositoryId = model.RepositoryId.ToString(), Expanded = expanded }).Value;

    /// <summary>
    /// Shop.Core: Shop (no types) → Shop.Core (no types) → { Shop.Core.Orders [Order, Line], Shop.Core.Billing [Invoice] }.
    /// Shop.Web: Shop.Web [Controller → uses Order, calls Invoice.Total]. Project reference Web → Core.
    /// </summary>
    private (ArchitectureModel Model, Ids Ids) Sample()
    {
        var core = _builder.Project("Shop.Core", "Order.cs");
        var web = _builder.Project("Shop.Web", "Controller.cs");
        var shop = _builder.Module(core, "Shop");
        var shopCore = _builder.Module(core, "Shop.Core", shop);
        var orders = _builder.Module(core, "Shop.Core.Orders", shopCore);
        var billing = _builder.Module(core, "Shop.Core.Billing", shopCore);
        var order = _builder.Type(core, orders, "Order");
        var line = _builder.Type(core, orders, "Line");
        var invoice = _builder.Type(core, billing, "Invoice");
        var total = _builder.Member(core, billing, invoice, "Total");
        var webModule = _builder.Module(web, "Shop.Web");
        var controller = _builder.Type(web, webModule, "Controller");

        _builder.ProjectReference(web, core);
        _builder.Relate(ElementKind.Entity, controller.Id, RelationshipKind.UsesType, ElementKind.Entity, order.Id);
        _builder.Relate(ElementKind.Entity, controller.Id, RelationshipKind.Calls, ElementKind.Entity, total.Id);
        _builder.Relate(ElementKind.Entity, order.Id, RelationshipKind.UsesType, ElementKind.Entity, line.Id);
        _builder.Relate(ElementKind.Entity, line.Id, RelationshipKind.UsesType, ElementKind.Entity, invoice.Id);

        return (_builder.Build(), new Ids(core.Id.ToString(), web.Id.ToString(), shopCore.Id.ToString(), orders.Id.ToString(),
            billing.Id.ToString(), order.Id.ToString(), line.Id.ToString(), invoice.Id.ToString(), controller.Id.ToString(), webModule.Id.ToString()));
    }

    [Fact]
    public void Collapsed_ShowsProjectsOnly_WhichAreExpandable()
    {
        var (model, ids) = Sample();

        var view = Project(model);

        Assert.Equal(["Shop.Core", "Shop.Web"], view.Nodes.Select(n => n.Label));
        Assert.All(view.Nodes, n => Assert.True(n.IsExpandable && !n.IsExpanded));
        var edge = Assert.Single(view.Edges);
        Assert.Equal((ids.Web, ids.Core, 3), (edge.Source, edge.Target, edge.Count)); // reference + 2 code relationships
    }

    [Fact]
    public void ExpandedProject_ShowsCompressedNamespaces_AsChildren()
    {
        var (model, ids) = Sample();

        var view = Project(model, ids.Core);

        var module = Assert.Single(view.Nodes, n => n.Kind == GraphNodeKinds.Module);
        Assert.Equal((ids.ShopCore, ids.Core, "Shop.Core"), (module.Id, module.ParentId, module.Label)); // "Shop" is skipped
        Assert.True(module.IsExpandable);
        Assert.Equal(2, module.ChildCount);
        Assert.Equal(4, module.Metrics.Size); // entities below the module
        Assert.True(view.Nodes.Single(n => n.Id == ids.Core).IsExpanded);

        // Code relationships: Web → module (deepest visible node of Order and Total); the project reference stays on the
        // (now compound) project node; no edge between the compound and its own content.
        Assert.Equal(
            [(ids.Web, ids.Core, 1), (ids.Web, ids.ShopCore, 2)],
            view.Edges.Select(e => (e.Source, e.Target, e.Count)).OrderBy(e => e.Target == ids.Core ? 0 : 1));
    }

    [Fact]
    public void ExpandedModules_ShowTypesAndSubNamespaces_WithRelativeLabels()
    {
        var (model, ids) = Sample();

        var view = Project(model, ids.Core, ids.ShopCore, ids.Orders, ids.Web, ids.WebModule);

        Assert.Equal("Orders", view.Nodes.Single(n => n.Id == ids.Orders).Label);
        Assert.Equal("Billing", view.Nodes.Single(n => n.Id == ids.Billing).Label);
        var order = view.Nodes.Single(n => n.Id == ids.Order);
        Assert.Equal((GraphNodeKinds.Entity, ids.Orders, "class", false), (order.Kind, order.ParentId, order.Sublabel, order.IsExpandable));

        // Parents always come before their children.
        var position = view.Nodes.Select((n, i) => (n.Id, i)).ToDictionary(x => x.Id, x => x.i);
        Assert.All(view.Nodes.Where(n => n.ParentId is not null), n => Assert.True(position[n.ParentId!] < position[n.Id]));

        // Entity edges attach to the deepest visible ancestor: Total (member) → its type's module "Billing" (collapsed).
        var edges = view.Edges.Select(e => (e.Source, e.Target)).ToHashSet();
        Assert.Contains((ids.Controller, ids.Order), edges);
        Assert.Contains((ids.Controller, ids.Billing), edges);
        Assert.Contains((ids.Order, ids.Line), edges);
        Assert.Contains((ids.Line, ids.Billing), edges);
        // The project reference stays at project level (Web project → Core project).
        Assert.Contains((ids.Web, ids.Core), edges);
    }

    [Fact]
    public void ExpandingAndCollapsing_IsReversible()
    {
        var (model, ids) = Sample();

        var before = Project(model);
        Project(model, ids.Core, ids.ShopCore);
        var after = Project(model);
        var partlyBefore = Project(model, ids.Core);
        Project(model, ids.Core, ids.ShopCore, ids.Billing);
        var partlyAfter = Project(model, ids.Core);

        Assert.Equal(Serialize(before), Serialize(after));
        Assert.Equal(Serialize(partlyBefore), Serialize(partlyAfter));
    }

    [Fact]
    public void ExpandedModuleOfACollapsedProject_AndUnknownIds_AreIgnored()
    {
        var (model, ids) = Sample();

        var view = Project(model, ids.ShopCore, "not-an-id", new string('f', 32), ids.Order);

        Assert.Equal(Serialize(Project(model)), Serialize(view));
    }

    [Fact]
    public void EdgeDetails_FollowTheExpandedView()
    {
        var (model, ids) = Sample();
        var query = new GraphQuery { RepositoryId = model.RepositoryId.ToString(), Expanded = [ids.Core, ids.ShopCore, ids.Orders] };

        var details = _projector.DescribeEdge(model, query, $"{ids.Web}|{ids.Order}").Value;

        Assert.Equal(("Shop.Web", "Order"), (details.SourceLabel, details.TargetLabel));
        var relationship = Assert.Single(details.Relationships);
        Assert.Equal(("uses-type", "Shop.Web.Controller", "Shop.Core.Orders.Order"), (relationship.Kind, relationship.SourceLabel, relationship.TargetLabel));
    }

    private static string Serialize(GraphViewDto view) => System.Text.Json.JsonSerializer.Serialize(view, GraphJsonContext.Default.GraphViewDto);

    private sealed record Ids(string Core, string Web, string ShopCore, string Orders, string Billing, string Order, string Line, string Invoice, string Controller, string WebModule);
}
