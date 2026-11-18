using System.Text.Json;
using CodebaseExplorer.Application.Graph;
using CodebaseExplorer.Contracts.Graph;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Evidence;
using CodebaseExplorer.Domain.Model;
using CodebaseExplorer.Domain.Relationships;
using CodebaseExplorer.Graph.Projection;

namespace CodebaseExplorer.Graph.Tests.Projection;

public sealed class GraphProjectorTests
{
    private readonly GraphProjector _projector = new();

    private GraphViewDto Project(ArchitectureModel model, bool includeExternal = false) =>
        _projector.Project(model, new GraphQuery
        {
            RepositoryId = model.RepositoryId.ToString(),
            IncludeExternalDependencies = includeExternal,
        }).Value;

    [Fact]
    public void Projects_BecomeNodesWithMetadata()
    {
        var builder = new ModelBuilder();
        var billing = builder.Project("Billing", "Invoice.cs", "Tax.cs");
        builder.Project("Api", "Program.cs");
        var root = builder.Module(billing, "Billing");
        builder.Module(billing, "Billing.Invoices", root);
        builder.Module(billing, "Shared");

        var view = Project(builder.Build());

        Assert.Equal(GraphViewDto.CurrentSchemaVersion, view.SchemaVersion);
        Assert.Equal(GraphLevels.Project, view.Level);
        Assert.Equal(builder.Repository.ToString(), view.RepositoryId);
        Assert.Equal(["Api", "Billing"], view.Nodes.Select(n => n.Label));

        var node = view.Nodes.Single(n => n.Label == "Billing");
        Assert.Equal(billing.Id.ToString(), node.Id);
        Assert.Equal(GraphNodeKinds.Project, node.Kind);
        Assert.Equal("src/Billing", node.Sublabel);
        Assert.Equal("csharp", node.Language);
        Assert.Equal("dotnet", node.Ecosystem);
        Assert.Equal(3, node.Metrics.Size); // manifest + 2 sources
        Assert.Equal(2, node.ChildCount); // top-level modules only
        Assert.True(node.IsExpandable); // GRAPH-006: projects with modules can be expanded
        Assert.Null(node.ParentId);
        Assert.Empty(node.Badges);
        Assert.Empty(view.Edges);
    }

    [Fact]
    public void PlaceholderProject_HasMissingBadge()
    {
        var builder = new ModelBuilder();
        var infrastructure = builder.Project("Infrastructure", "A.cs");
        var legacy = builder.Placeholder("Legacy");
        builder.ProjectReference(infrastructure, legacy, EvidenceLevel.Syntactic);

        var view = Project(builder.Build());

        var node = view.Nodes.Single(n => n.Label == "Legacy");
        Assert.Equal([GraphBadges.Missing], node.Badges);
        var edge = Assert.Single(view.Edges);
        Assert.Equal(GraphEvidenceLevels.Syntactic, edge.MaxLevel);
    }

    [Fact]
    public void ProjectReference_BecomesDeclaredEdge()
    {
        var builder = new ModelBuilder();
        var api = builder.Project("Api", "Program.cs");
        var billing = builder.Project("Billing", "Invoice.cs");
        builder.ProjectReference(api, billing);

        var edge = Assert.Single(Project(builder.Build()).Edges);

        Assert.Equal($"{api.Id}|{billing.Id}", edge.Id);
        Assert.Equal(api.Id.ToString(), edge.Source);
        Assert.Equal(billing.Id.ToString(), edge.Target);
        Assert.Equal(1, edge.Count);
        Assert.Equal(1, edge.Occurrences);
        Assert.Equal(new Dictionary<string, int> { ["project-reference"] = 1 }, edge.Kinds);
        Assert.Equal(GraphEvidenceLevels.Confirmed, edge.MaxLevel);
        Assert.Equal(GraphEvidenceLevels.Confirmed, edge.MinLevel);
        Assert.True(edge.IsDeclared);
    }

    [Fact]
    public void CodeRelationships_AreAggregatedIntoTheProjectEdge()
    {
        var builder = new ModelBuilder();
        var api = builder.Project("Api", "Program.cs");
        var billing = builder.Project("Billing", "Invoice.cs");
        var apiModule = builder.Module(api, "Api");
        var billingModule = builder.Module(billing, "Billing");
        var controller = builder.Type(api, apiModule, "Controller");
        var handler = builder.Type(api, apiModule, "Handler");
        var invoice = builder.Type(billing, billingModule, "Invoice");
        var service = builder.Type(billing, billingModule, "Service");

        builder.ProjectReference(api, billing);
        builder.Relate(ElementKind.Entity, controller.Id, RelationshipKind.UsesType, ElementKind.Entity, invoice.Id,
            EvidenceLevel.Confirmed, EvidenceLevel.Syntactic);
        builder.Relate(ElementKind.Entity, handler.Id, RelationshipKind.UsesType, ElementKind.Entity, service.Id, EvidenceLevel.Heuristic);
        builder.Relate(ElementKind.Entity, handler.Id, RelationshipKind.Calls, ElementKind.Entity, service.Id);

        var edge = Assert.Single(Project(builder.Build()).Edges);

        Assert.Equal(4, edge.Count);
        Assert.Equal(5, edge.Occurrences);
        Assert.Equal(new Dictionary<string, int> { ["calls"] = 1, ["project-reference"] = 1, ["uses-type"] = 2 }, edge.Kinds);
        Assert.Equal(["calls", "project-reference", "uses-type"], edge.Kinds.Keys);
        Assert.Equal(GraphEvidenceLevels.Confirmed, edge.MaxLevel);
        Assert.Equal(GraphEvidenceLevels.Heuristic, edge.MinLevel);
        Assert.True(edge.IsDeclared);
    }

    [Fact]
    public void CodeOnlyEdge_IsNotDeclared_AndFileEndpointsMapToTheirProject()
    {
        var builder = new ModelBuilder();
        var web = builder.Project("Web", "app.ts");
        var shared = builder.Project("Shared", "util.ts");
        var webFile = RepositoryFileId(builder, "src/Web/app.ts");
        var sharedFile = RepositoryFileId(builder, "src/Shared/util.ts");
        builder.Relate(ElementKind.File, webFile, RelationshipKind.Imports, ElementKind.File, sharedFile, EvidenceLevel.Syntactic);

        var edge = Assert.Single(Project(builder.Build()).Edges);

        Assert.Equal(web.Id.ToString(), edge.Source);
        Assert.Equal(shared.Id.ToString(), edge.Target);
        Assert.False(edge.IsDeclared);
        Assert.Equal(new Dictionary<string, int> { ["imports"] = 1 }, edge.Kinds);
    }

    [Fact]
    public void RelationshipsInsideOneProject_AreNotShown()
    {
        var builder = new ModelBuilder();
        var billing = builder.Project("Billing", "Invoice.cs");
        var module = builder.Module(billing, "Billing");
        var a = builder.Type(billing, module, "A");
        var b = builder.Type(billing, module, "B");
        builder.Relate(ElementKind.Entity, a.Id, RelationshipKind.UsesType, ElementKind.Entity, b.Id);

        var view = Project(builder.Build());

        Assert.Empty(view.Edges);
        Assert.Equal(0, view.Nodes.Single().Metrics.FanOut);
    }

    [Fact]
    public void ExternalDependencies_AreHiddenByDefault()
    {
        var builder = new ModelBuilder();
        var api = builder.Project("Api", "Program.cs");
        var serilog = builder.Package("Serilog");
        builder.Relate(ElementKind.Project, api.Id, RelationshipKind.PackageReference, ElementKind.ExternalDependency, serilog.Id);

        var view = Project(builder.Build());

        Assert.Equal(["Api"], view.Nodes.Select(n => n.Label));
        Assert.Empty(view.Edges);
    }

    [Fact]
    public void ExternalDependencies_CanBeIncluded()
    {
        var builder = new ModelBuilder();
        var api = builder.Project("Api", "Program.cs");
        var serilog = builder.Package("Serilog");
        builder.Relate(ElementKind.Project, api.Id, RelationshipKind.PackageReference, ElementKind.ExternalDependency, serilog.Id);

        var view = Project(builder.Build(), includeExternal: true);

        Assert.Equal(["Api", "Serilog"], view.Nodes.Select(n => n.Label));
        var package = view.Nodes[1];
        Assert.Equal(GraphNodeKinds.ExternalDependency, package.Kind);
        Assert.Equal("nuget", package.Ecosystem);
        Assert.Equal(1, package.Metrics.FanIn);
        var edge = Assert.Single(view.Edges);
        Assert.True(edge.IsDeclared);
        Assert.Equal(new Dictionary<string, int> { ["package-reference"] = 1 }, edge.Kinds);
    }

    [Fact]
    public void FanInAndFanOut_CountDistinctNeighbors()
    {
        var builder = new ModelBuilder();
        var a = builder.Project("A", "a.cs");
        var b = builder.Project("B", "b.cs");
        var c = builder.Project("C", "c.cs");
        builder.ProjectReference(a, b);
        builder.ProjectReference(a, c);
        builder.ProjectReference(b, c);

        var nodes = Project(builder.Build()).Nodes.ToDictionary(n => n.Label);

        Assert.Equal((0, 2), (nodes["A"].Metrics.FanIn, nodes["A"].Metrics.FanOut));
        Assert.Equal((1, 1), (nodes["B"].Metrics.FanIn, nodes["B"].Metrics.FanOut));
        Assert.Equal((2, 0), (nodes["C"].Metrics.FanIn, nodes["C"].Metrics.FanOut));
    }

    [Fact]
    public void QueryForOtherRepository_IsRejected()
    {
        var model = new ModelBuilder().Build();

        var result = _projector.Project(model, new GraphQuery { RepositoryId = RepositoryId.New().ToString() });

        Assert.Equal(GraphErrorCodes.RepositoryMismatch, result.Error.Code);
    }

    [Theory]
    [InlineData(GraphLevels.Module)]
    [InlineData(GraphLevels.Entity)]
    [InlineData("unknown")]
    public void UnsupportedLevels_AreRejected(string level)
    {
        var model = new ModelBuilder().Build();

        var result = _projector.Project(model, new GraphQuery { RepositoryId = model.RepositoryId.ToString(), Level = level });

        Assert.Equal(GraphErrorCodes.LevelNotSupported, result.Error.Code);
    }

    [Fact]
    public void Projection_IsDeterministic()
    {
        static ArchitectureModel Build(bool reversed)
        {
            var builder = new ModelBuilder();
            var names = reversed ? new[] { "C", "B", "A" } : ["A", "B", "C"];
            var projects = names.Select(n => builder.Project(n, $"{n}.cs")).ToDictionary(p => p.Name);
            foreach (var (source, target) in reversed ? new[] { ("B", "C"), ("A", "C"), ("A", "B") } : [("A", "B"), ("A", "C"), ("B", "C")])
            {
                builder.ProjectReference(projects[source], projects[target]);
            }

            return builder.Build();
        }

        var first = JsonSerializer.Serialize(Project(Build(reversed: false)), GraphJsonContext.Default.GraphViewDto);
        var second = JsonSerializer.Serialize(Project(Build(reversed: true)), GraphJsonContext.Default.GraphViewDto);

        Assert.Equal(first, second);
    }

    private static ElementId RepositoryFileId(ModelBuilder builder, string path) =>
        Domain.Files.RepositoryFile.IdFor(builder.Repository, RelativePath.Create(path));
}
