using CodebaseExplorer.Application.Graph;
using CodebaseExplorer.Contracts.Graph;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Model;
using CodebaseExplorer.Domain.Modules;
using CodebaseExplorer.Domain.Projects;
using CodebaseExplorer.Domain.Relationships;
using CodebaseExplorer.Graph.Projection;

namespace CodebaseExplorer.Graph.Tests.Projection;

/// <summary>GRAPH-008: no view exceeds the node budget; surplus roots collapse into "N more", oversized expansions are refused.</summary>
public sealed class GraphNodeBudgetTests
{
    private readonly GraphProjector _projector = new();

    /// <summary>
    /// A random but reproducible model: <paramref name="projects"/> projects, each with up to 4 modules and up to 3 types per
    /// module, packages, and random relationships at all levels.
    /// </summary>
    private static (ArchitectureModel Model, List<string> Expandable) RandomModel(int seed, int projects)
    {
        var builder = new ModelBuilder();
        var random = new Random(seed);
        var allProjects = new List<CodeProject>();
        var entities = new List<(CodeProject Project, Domain.Entities.CodeEntity Entity)>();
        var expandable = new List<string>();

        for (var p = 0; p < projects; p++)
        {
            var project = builder.Project($"P{p:D3}", $"F{p}.cs");
            allProjects.Add(project);
            expandable.Add(project.Id.ToString());
            CodeModule? parent = null;
            for (var m = 0; m < random.Next(0, 5); m++)
            {
                var module = builder.Module(project, $"P{p:D3}.M{m}", random.Next(2) == 0 ? parent : null);
                expandable.Add(module.Id.ToString());
                parent = module;
                for (var t = 0; t < random.Next(0, 4); t++)
                {
                    entities.Add((project, builder.Type(project, module, $"T{t}")));
                }
            }
        }

        var packages = Enumerable.Range(0, 5).Select(i => builder.Package($"pkg{i}")).ToArray();
        var related = new HashSet<(ElementId, RelationshipKind, ElementId)>();
        for (var r = 0; r < projects * 2; r++)
        {
            var source = allProjects[random.Next(allProjects.Count)];
            var target = allProjects[random.Next(allProjects.Count)];
            if (source.Id != target.Id && related.Add((source.Id, RelationshipKind.ProjectReference, target.Id)))
            {
                builder.ProjectReference(source, target);
            }

            var package = packages[random.Next(packages.Length)];
            if (related.Add((source.Id, RelationshipKind.PackageReference, package.Id)))
            {
                builder.Relate(ElementKind.Project, source.Id, RelationshipKind.PackageReference, ElementKind.ExternalDependency, package.Id);
            }
        }

        for (var r = 0; r < entities.Count; r++)
        {
            var (_, a) = entities[random.Next(entities.Count)];
            var (_, b) = entities[random.Next(entities.Count)];
            if (a.Id != b.Id && related.Add((a.Id, RelationshipKind.UsesType, b.Id)))
            {
                builder.Relate(ElementKind.Entity, a.Id, RelationshipKind.UsesType, ElementKind.Entity, b.Id);
            }
        }

        return (builder.Build(), expandable);
    }

    public static TheoryData<int, int, int, bool, int> Cases()
    {
        var data = new TheoryData<int, int, int, bool, int>();
        foreach (var seed in new[] { 1, 2, 3 })
        {
            foreach (var (projects, budget) in new[] { (5, 2), (5, 50), (40, 10), (40, 41), (120, 30), (120, 500) })
            {
                data.Add(seed, projects, budget, seed % 2 == 0, seed * 7);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void No_view_exceeds_the_budget(int seed, int projects, int budget, bool withPackages, int expandedCount)
    {
        var (model, expandable) = RandomModel(seed, projects);
        var random = new Random(seed);
        var expanded = expandable.OrderBy(_ => random.Next()).Take(expandedCount).ToArray();

        var view = _projector.Project(model, new GraphQuery
        {
            RepositoryId = model.RepositoryId.ToString(),
            NodeBudget = budget,
            IncludeExternalDependencies = withPackages,
            Expanded = expanded,
        }).Value;

        Assert.True(view.Nodes.Count <= budget, $"{view.Nodes.Count} nodes for a budget of {budget}");
        var ids = view.Nodes.Select(n => n.Id).ToHashSet();
        Assert.All(view.Edges, e => Assert.True(ids.Contains(e.Source) && ids.Contains(e.Target)));
        Assert.All(view.Nodes.Where(n => n.ParentId is not null), n => Assert.Contains(n.ParentId!, ids));

        var roots = projects + (withPackages ? 5 : 0);
        var aggregate = view.Nodes.SingleOrDefault(n => n.Kind == GraphNodeKinds.Aggregate);
        if (roots > budget)
        {
            Assert.NotNull(aggregate);
            Assert.Equal($"{roots - (budget - 1)} more", aggregate.Label);
            Assert.Equal(TruncationReasons.NodeBudget, view.Truncation?.Reason);
        }
        else
        {
            Assert.Null(aggregate);
        }

        // Without truncation the view equals the unlimited projection.
        if (view.Truncation is null)
        {
            var unlimited = _projector.Project(model, new GraphQuery
            {
                RepositoryId = model.RepositoryId.ToString(),
                NodeBudget = GraphQuery.MaxNodeBudget,
                IncludeExternalDependencies = withPackages,
                Expanded = expanded,
            }).Value;
            Assert.Equal(unlimited.Nodes.Select(n => n.Id), view.Nodes.Select(n => n.Id));
        }
    }

    [Fact]
    public void The_roots_with_the_highest_degree_stay_and_the_aggregate_takes_the_other_edges()
    {
        var builder = new ModelBuilder();
        var hub = builder.Project("Hub", "Hub.cs");
        var spokes = Enumerable.Range(0, 4).Select(i => builder.Project($"S{i}", $"S{i}.cs")).ToArray();
        var lonely = builder.Project("Lonely", "Lonely.cs");
        foreach (var spoke in spokes)
        {
            builder.ProjectReference(spoke, hub);
        }

        builder.ProjectReference(lonely, spokes[0]);
        var model = builder.Build();

        var view = _projector.Project(model, new GraphQuery { RepositoryId = model.RepositoryId.ToString(), NodeBudget = 3 }).Value;

        // Degrees: Hub 4, S0 2, others 1 → Hub and S0 stay; S1–S3 and Lonely collapse.
        Assert.Equal(["Hub", "S0", "4 more"], view.Nodes.Select(n => n.Label));
        var labels = view.Nodes.ToDictionary(n => n.Id, n => n.Label);
        Assert.Equal(
            [("4 more", "Hub", 3), ("4 more", "S0", 1), ("S0", "Hub", 1)],
            view.Edges.Select(e => (labels[e.Source], labels[e.Target], e.Count)).Order());
        Assert.Equal(4, view.Truncation!.OmittedNodeCount);
    }

    [Fact]
    public void An_expansion_that_does_not_fit_is_refused_with_a_hint()
    {
        var builder = new ModelBuilder();
        var big = builder.Project("Big", "Big.cs");
        foreach (var i in Enumerable.Range(0, 10))
        {
            builder.Module(big, $"Big.M{i}");
        }

        var model = builder.Build();

        var view = _projector.Project(model, new GraphQuery { RepositoryId = model.RepositoryId.ToString(), NodeBudget = 5, Expanded = [big.Id.ToString()] }).Value;

        var node = Assert.Single(view.Nodes);
        Assert.False(node.IsExpanded);
        Assert.Equal((10, TruncationReasons.ExpansionRefused), (view.Truncation!.OmittedNodeCount, view.Truncation.Reason));
        Assert.True(_projector.Project(model, new GraphQuery { RepositoryId = model.RepositoryId.ToString(), NodeBudget = 11, Expanded = [big.Id.ToString()] })
            .Value.Nodes[0].IsExpanded);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(GraphQuery.MaxNodeBudget + 1)]
    public void Invalid_budgets_are_rejected(int budget)
    {
        var model = new ModelBuilder().Build();

        var result = _projector.Project(model, new GraphQuery { RepositoryId = model.RepositoryId.ToString(), NodeBudget = budget });

        Assert.Equal(GraphErrorCodes.NodeBudgetInvalid, result.Error.Code);
    }

    [Fact]
    public void The_aggregate_node_has_a_stable_id_and_edge_details_work_for_it()
    {
        var builder = new ModelBuilder();
        var projects = Enumerable.Range(0, 4).Select(i => builder.Project($"P{i}", $"P{i}.cs")).ToArray();
        builder.ProjectReference(projects[3], projects[0]);
        var model = builder.Build();
        var query = new GraphQuery { RepositoryId = model.RepositoryId.ToString(), NodeBudget = 2 };

        var first = _projector.Project(model, query).Value;
        var second = _projector.Project(model, query).Value;
        var aggregate = first.Nodes.Single(n => n.Kind == GraphNodeKinds.Aggregate);

        Assert.Equal(aggregate.Id, second.Nodes.Single(n => n.Kind == GraphNodeKinds.Aggregate).Id);
        var edge = Assert.Single(first.Edges);
        Assert.True(_projector.DescribeEdge(model, query, edge.Id).IsSuccess);
    }
}
