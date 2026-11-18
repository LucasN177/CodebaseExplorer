using CodebaseExplorer.Application.Graph;
using CodebaseExplorer.Contracts.Graph;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Dependencies;
using CodebaseExplorer.Domain.Evidence;
using CodebaseExplorer.Domain.Files;
using CodebaseExplorer.Domain.Model;
using CodebaseExplorer.Domain.Projects;
using CodebaseExplorer.Domain.Relationships;
using CodebaseExplorer.Graph.Projection;

namespace CodebaseExplorer.Graph.Tests.Projection;

/// <summary>GRAPH-007: filter combinations, neighborhood view and breadcrumbs of the projection.</summary>
public sealed class GraphFilterAndNeighborhoodTests
{
    private readonly GraphProjector _projector = new();

    /// <summary>
    /// Api (C#, dotnet) → Core project reference (confirmed); Api.Controller uses Core.Order (syntactic); a test type in Api
    /// calls Order (evidence only in a test file); Api.Tests (test project) → Api; Web (TS, node) imports Order
    /// (heuristic); Core's generated type uses Controller (evidence only in a generated file); packages Serilog (NuGet)
    /// and react (npm).
    /// </summary>
    private sealed class Sample
    {
        public Sample()
        {
            var builder = new ModelBuilder();
            Api = builder.Project("Api", Ecosystem.DotNet, LanguageId.CSharp, ("Api.cs", FileClassification.Source), ("ApiTests.cs", FileClassification.Test));
            Core = builder.Project("Core", Ecosystem.DotNet, LanguageId.CSharp, ("Order.cs", FileClassification.Source), ("Order.g.cs", FileClassification.Generated));
            Tests = builder.Project("Api.Tests", Ecosystem.DotNet, LanguageId.CSharp, ("Tests.cs", FileClassification.Test));
            Web = builder.Project("Web", Ecosystem.Node, LanguageId.TypeScript, ("app.ts", FileClassification.Source));

            var apiModule = builder.Module(Api, "Api");
            var coreModule = builder.Module(Core, "Core");
            var webModule = builder.Module(Web, "src");
            Controller = builder.TypeIn(Api, apiModule, "Controller", "Api.cs");
            ControllerTests = builder.TypeIn(Api, apiModule, "ControllerTests", "ApiTests.cs");
            Order = builder.TypeIn(Core, coreModule, "Order", "Order.cs");
            Generated = builder.TypeIn(Core, coreModule, "OrderSerializer", "Order.g.cs");
            App = builder.TypeIn(Web, webModule, "App", "app.ts");

            builder.RelateIn("src/Api/manifest", ElementKind.Project, Api.Id, RelationshipKind.ProjectReference, ElementKind.Project, Core.Id);
            builder.RelateIn("src/Api/Api.cs", ElementKind.Entity, Controller.Id, RelationshipKind.UsesType, ElementKind.Entity, Order.Id, EvidenceLevel.Syntactic);
            builder.RelateIn("src/Api/ApiTests.cs", ElementKind.Entity, ControllerTests.Id, RelationshipKind.Calls, ElementKind.Entity, Order.Id);
            builder.RelateIn("src/Api.Tests/Tests.cs", ElementKind.Project, Tests.Id, RelationshipKind.ProjectReference, ElementKind.Project, Api.Id);
            builder.RelateIn("src/Web/app.ts", ElementKind.Entity, App.Id, RelationshipKind.Imports, ElementKind.Entity, Order.Id, EvidenceLevel.Heuristic);
            builder.RelateIn("src/Core/Order.g.cs", ElementKind.Entity, Generated.Id, RelationshipKind.UsesType, ElementKind.Entity, Controller.Id);
            var serilog = builder.Package("Serilog", PackageEcosystem.NuGet);
            var react = builder.Package("react", PackageEcosystem.Npm);
            builder.RelateIn("src/Api/manifest", ElementKind.Project, Api.Id, RelationshipKind.PackageReference, ElementKind.ExternalDependency, serilog.Id);
            builder.RelateIn("src/Web/manifest", ElementKind.Project, Web.Id, RelationshipKind.PackageReference, ElementKind.ExternalDependency, react.Id);
            Model = builder.Build();
        }

        public ArchitectureModel Model { get; }

        public CodeProject Api { get; }

        public CodeProject Core { get; }

        public CodeProject Tests { get; }

        public CodeProject Web { get; }

        public Domain.Entities.CodeEntity Controller { get; }

        public Domain.Entities.CodeEntity ControllerTests { get; }

        public Domain.Entities.CodeEntity Order { get; }

        public Domain.Entities.CodeEntity Generated { get; }

        public Domain.Entities.CodeEntity App { get; }
    }

    private readonly Sample _sample = new();

    private GraphViewDto Project(GraphQuery query) => _projector.Project(_sample.Model, query).Value;

    private GraphQuery Query(GraphFilters? filters = null, bool packages = false, params string[] expanded) => new()
    {
        RepositoryId = _sample.Model.RepositoryId.ToString(),
        Filters = filters ?? GraphFilters.Default,
        IncludeExternalDependencies = packages,
        Expanded = expanded,
    };

    private static string[] Edges(GraphViewDto view)
    {
        var labels = view.Nodes.ToDictionary(n => n.Id, n => n.Label);
        return [.. view.Edges.Select(e => $"{labels[e.Source]}->{labels[e.Target]}:{e.Count}").Order(StringComparer.Ordinal)];
    }

    [Fact]
    public void By_default_tests_and_generated_code_are_hidden()
    {
        var view = Project(Query());

        Assert.Equal(["Api", "Core", "Web"], view.Nodes.Select(n => n.Label));
        // Api→Core: project reference + Controller uses Order (the test call and the generated usage are gone).
        Assert.Equal(["Api->Core:2", "Web->Core:1"], Edges(view));
    }

    [Fact]
    public void Including_tests_and_generated_code_shows_them()
    {
        var view = Project(Query(new GraphFilters { IncludeTests = true, IncludeGenerated = true }));

        Assert.Equal(["Api", "Api.Tests", "Core", "Web"], view.Nodes.Select(n => n.Label));
        Assert.Equal(["Api->Core:3", "Api.Tests->Api:1", "Core->Api:1", "Web->Core:1"], Edges(view));
    }

    [Fact]
    public void Hidden_test_types_disappear_from_expanded_modules()
    {
        var apiModule = _sample.Model.Modules.Single(m => m.QualifiedName == "Api").Id.ToString();

        var hidden = Project(Query(null, false, _sample.Api.Id.ToString(), apiModule));
        var shown = Project(Query(new GraphFilters { IncludeTests = true }, false, _sample.Api.Id.ToString(), apiModule));

        Assert.DoesNotContain(hidden.Nodes, n => n.Label == "ControllerTests");
        Assert.Contains(shown.Nodes, n => n.Label == "ControllerTests");
    }

    [Theory]
    [InlineData("csharp", null, "Api,Core")]
    [InlineData("typescript", null, "Web")]
    [InlineData(null, "node", "Web")]
    [InlineData("csharp", "node", "")]
    [InlineData("csharp,typescript", "dotnet,node", "Api,Core,Web")]
    public void Languages_and_ecosystems_filter_projects(string? languages, string? ecosystems, string expected)
    {
        var view = Project(Query(new GraphFilters
        {
            Languages = languages?.Split(',') ?? [],
            Ecosystems = ecosystems?.Split(',') ?? [],
        }));

        Assert.Equal(expected, string.Join(',', view.Nodes.Select(n => n.Label)));
    }

    [Fact]
    public void Ecosystems_filter_external_dependencies_by_package_manager()
    {
        var view = Project(Query(new GraphFilters { Ecosystems = ["node"] }, packages: true));

        Assert.Equal(["Web", "react"], view.Nodes.Select(n => n.Label));
        Assert.Equal(["Web->react:1"], Edges(view));
    }

    [Theory]
    [InlineData("project-reference", null, "Api->Core:1")]
    [InlineData("uses-type,imports", null, "Api->Core:1,Web->Core:1")]
    [InlineData(null, "syntactic", "Api->Core:2")]
    [InlineData(null, "confirmed", "Api->Core:1")]
    [InlineData("imports", "syntactic", "")]
    public void Relationship_kinds_and_minimum_level_filter_edges(string? kinds, string? minLevel, string expected)
    {
        var view = Project(Query(new GraphFilters { RelationshipKinds = kinds?.Split(',') ?? [], MinLevel = minLevel }));

        Assert.Equal(["Api", "Core", "Web"], view.Nodes.Select(n => n.Label)); // relationship filters never hide nodes
        Assert.Equal(expected, string.Join(',', Edges(view)));
    }

    [Theory]
    [InlineData("both", 1, "Web,Core")]
    [InlineData("both", 2, "Web,Core,Api")]
    [InlineData("outgoing", 1, "Web,Core")]
    [InlineData("incoming", 1, "Web")]
    public void Neighborhood_follows_the_direction(string direction, int depth, string expected)
    {
        var view = Project(Query() with { Focus = _sample.Web.Id.ToString(), Direction = direction, Depth = depth });

        Assert.Equal(expected.Split(',').Order(StringComparer.Ordinal), view.Nodes.Select(n => n.Label).Order(StringComparer.Ordinal));
        Assert.Equal(["Repository", "Web"], view.Breadcrumbs.Select(b => b.Label));
    }

    [Fact]
    public void Neighborhood_depth_two_reaches_further_and_keeps_compound_parents()
    {
        var coreModule = _sample.Model.Modules.Single(m => m.QualifiedName == "Core").Id.ToString();
        var query = Query(null, false, _sample.Core.Id.ToString(), coreModule) with { Focus = _sample.Order.Id.ToString(), Depth = 1, Direction = GraphDirections.Incoming };

        var view = Project(query);

        // Order is inside Core › Core; incoming at depth 1: Api (uses Order) and Web (imports Order).
        Assert.Equal(["Api", "Core", "Core", "Order", "Web"], view.Nodes.Select(n => n.Label).Order(StringComparer.Ordinal));
        Assert.Equal(["Repository", "Core", "Core", "Order"], view.Breadcrumbs.Select(b => b.Label));
        Assert.Equal([GraphLevels.Repository, GraphLevels.Project, GraphLevels.Module, GraphLevels.Entity], view.Breadcrumbs.Select(b => b.Level));
    }

    [Fact]
    public void Neighborhood_respects_the_node_budget_nearest_first()
    {
        var view = Project(Query() with { Focus = _sample.Core.Id.ToString(), NodeBudget = 2 });

        Assert.Equal(2, view.Nodes.Count);
        Assert.Contains(view.Nodes, n => n.Label == "Core");
        Assert.Equal((1, TruncationReasons.NodeBudget), (view.Truncation!.OmittedNodeCount, view.Truncation.Reason));
    }

    [Fact]
    public void Focus_on_a_model_element_uses_its_deepest_visible_node()
    {
        var view = Project(Query() with { Focus = _sample.Order.Id.ToString(), Direction = GraphDirections.Incoming });

        Assert.Equal(["Api", "Core", "Web"], view.Nodes.Select(n => n.Label)); // Order is shown by its project
    }

    [Fact]
    public void Focus_on_a_hidden_or_unknown_element_is_an_error()
    {
        var hidden = _projector.Project(_sample.Model, Query() with { Focus = _sample.Tests.Id.ToString() });
        var unknown = _projector.Project(_sample.Model, Query() with { Focus = "not-an-id" });

        Assert.Equal(GraphErrorCodes.FocusNotFound, hidden.Error.Code);
        Assert.Equal(GraphErrorCodes.FocusNotFound, unknown.Error.Code);
    }

    [Theory]
    [InlineData(0, GraphDirections.Both)]
    [InlineData(4, GraphDirections.Both)]
    [InlineData(1, "sideways")]
    public void Invalid_neighborhood_settings_are_rejected(int depth, string direction)
    {
        var result = _projector.Project(_sample.Model, Query() with { Focus = _sample.Web.Id.ToString(), Depth = depth, Direction = direction });

        Assert.Equal(GraphErrorCodes.NeighborhoodInvalid, result.Error.Code);
    }

    [Fact]
    public void Edge_details_follow_the_filters()
    {
        var query = Query(new GraphFilters { MinLevel = GraphEvidenceLevels.Confirmed });
        var edge = Project(query).Edges.Single();

        var details = _projector.DescribeEdge(_sample.Model, query, edge.Id).Value;

        Assert.Equal(["project-reference"], details.Relationships.Select(r => r.Kind));
    }

    [Fact]
    public void Without_focus_the_breadcrumbs_show_the_repository()
    {
        Assert.Equal([GraphLevels.Repository], Project(Query()).Breadcrumbs.Select(b => b.Level));
    }
}
