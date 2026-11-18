using CodebaseExplorer.Application.Common;
using CodebaseExplorer.Application.Graph;
using CodebaseExplorer.Contracts.Graph;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Evidence;
using CodebaseExplorer.Domain.Model;
using CodebaseExplorer.Domain.Relationships;
using CodebaseExplorer.Graph.Projection;

namespace CodebaseExplorer.Graph.Tests.Projection;

/// <summary>GRAPH-010: the relationships and evidence behind one aggregated edge.</summary>
public sealed class GraphEdgeDetailsTests
{
    private readonly GraphProjector _projector = new();

    private static GraphQuery Query(ArchitectureModel model, bool includeExternal = false) =>
        new() { RepositoryId = model.RepositoryId.ToString(), IncludeExternalDependencies = includeExternal };

    [Fact]
    public void DescribeEdge_ListsEveryRelationshipOfTheEdge_WithEvidence()
    {
        var builder = new ModelBuilder();
        var web = builder.Project("Web", "Controller.cs");
        var billing = builder.Project("Billing", "Invoice.cs");
        var webModule = builder.Module(web, "Web");
        var billingModule = builder.Module(billing, "Billing");
        var controller = builder.Type(web, webModule, "Controller");
        var invoice = builder.Type(billing, billingModule, "Invoice");
        builder.ProjectReference(web, billing);
        builder.Relate(ElementKind.Entity, controller.Id, RelationshipKind.UsesType, ElementKind.Entity, invoice.Id,
            EvidenceLevel.Confirmed, EvidenceLevel.Syntactic);
        var model = builder.Build();
        var edge = Assert.Single(_projector.Project(model, Query(model)).Value.Edges);

        var details = _projector.DescribeEdge(model, Query(model), edge.Id).Value;

        Assert.Equal(edge.Id, details.EdgeId);
        Assert.Equal(("Web", "Billing"), (details.SourceLabel, details.TargetLabel));
        Assert.Equal(edge.Count, details.Relationships.Count);
        Assert.Equal(edge.Occurrences, details.Relationships.Sum(r => r.Evidence.Count));

        var reference = details.Relationships[0];
        Assert.Equal(("project-reference", "Web", "Billing", GraphEvidenceLevels.Confirmed),
            (reference.Kind, reference.SourceLabel, reference.TargetLabel, reference.Level));

        var usage = details.Relationships[1];
        Assert.Equal(("uses-type", "Web.Controller", "Billing.Invoice"), (usage.Kind, usage.SourceLabel, usage.TargetLabel));
        Assert.Equal([GraphEvidenceLevels.Confirmed, GraphEvidenceLevels.Syntactic], usage.Evidence.Select(e => e.Level));

        var evidence = usage.Evidence[0];
        Assert.Equal("test.analyzer", evidence.Analyzer);
        Assert.Equal("test.rule0", evidence.Rule);
        Assert.Equal("src/Web/Web.csproj", evidence.Path); // the builder records evidence in the first file
        Assert.Equal((1, 1), (evidence.StartLine, evidence.EndLine));
    }

    [Fact]
    public void DescribeEdge_FileRelationships_AreNamedByPath()
    {
        var builder = new ModelBuilder();
        var web = builder.Project("Web", "Page.cs");
        var shared = builder.Project("Shared", "Util.cs");
        builder.Relate(ElementKind.File, FileId(builder, "src/Web/Page.cs"), RelationshipKind.Imports,
            ElementKind.File, FileId(builder, "src/Shared/Util.cs"), EvidenceLevel.Syntactic);
        var model = builder.Build();

        var details = _projector.DescribeEdge(model, Query(model), $"{web.Id}|{shared.Id}").Value;

        var relationship = Assert.Single(details.Relationships);
        Assert.Equal(("src/Web/Page.cs", "src/Shared/Util.cs"), (relationship.SourceLabel, relationship.TargetLabel));
        Assert.Equal(GraphEvidenceLevels.Syntactic, relationship.Level);
    }

    [Fact]
    public void DescribeEdge_ExternalDependency_OnlyWhenIncluded()
    {
        var builder = new ModelBuilder();
        var web = builder.Project("Web", "A.cs");
        var package = builder.Package("Serilog");
        builder.Relate(ElementKind.Project, web.Id, RelationshipKind.PackageReference, ElementKind.ExternalDependency, package.Id);
        var model = builder.Build();
        var edgeId = $"{web.Id}|{package.Id}";

        var included = _projector.DescribeEdge(model, Query(model, includeExternal: true), edgeId).Value;
        var excluded = _projector.DescribeEdge(model, Query(model), edgeId);

        Assert.Equal("Serilog", included.TargetLabel);
        Assert.Equal(GraphErrorCodes.EdgeNotFound, excluded.Error.Code);
        Assert.Equal(ErrorKind.NotFound, excluded.Error.Kind);
    }

    [Theory]
    [InlineData("not-an-edge")]
    [InlineData("0000000000000000000000000000000a|0000000000000000000000000000000b")]
    public void DescribeEdge_UnknownEdge_IsNotFound(string edgeId)
    {
        var builder = new ModelBuilder();
        builder.ProjectReference(builder.Project("A", "A.cs"), builder.Project("B", "B.cs"));
        var model = builder.Build();

        Assert.Equal(GraphErrorCodes.EdgeNotFound, _projector.DescribeEdge(model, Query(model), edgeId).Error.Code);
    }

    [Fact]
    public void DescribeEdge_OtherRepository_IsRejected()
    {
        var builder = new ModelBuilder();
        var a = builder.Project("A", "A.cs");
        var b = builder.Project("B", "B.cs");
        builder.ProjectReference(a, b);
        var model = builder.Build();

        var result = _projector.DescribeEdge(model, new GraphQuery { RepositoryId = new string('f', 32) }, $"{a.Id}|{b.Id}");

        Assert.Equal(GraphErrorCodes.RepositoryMismatch, result.Error.Code);
    }

    private static ElementId FileId(ModelBuilder builder, string path) =>
        Domain.Files.RepositoryFile.IdFor(builder.Repository, RelativePath.Create(path));
}
