using CodebaseExplorer.Contracts.Graph;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Projects;
using CodebaseExplorer.Domain.Relationships;
using CodebaseExplorer.Graph.Projection;

namespace CodebaseExplorer.Graph.Tests.Projection;

/// <summary>GRAPH-009: repository level (L0 groups) and the folder graph of repositories without projects.</summary>
public sealed class GraphRepositoryLevelTests
{
    private readonly GraphProjector _projector = new();

    /// <summary>
    /// A workspace root with two members, two .NET projects in Shop.slnx (one also in Legacy.sln — the first path wins),
    /// one .NET project without solution and one Python project. Member → .NET reference and Python → member reference.
    /// </summary>
    private (Domain.Model.ArchitectureModel Model, Dictionary<string, CodeProject> Projects) Sample()
    {
        var builder = new ModelBuilder();
        var projects = new Dictionary<string, CodeProject>
        {
            ["mono"] = builder.GroupedProject("mono", ProjectKind.NodeWorkspaceRoot, Ecosystem.Node),
            ["ui"] = builder.GroupedProject("ui", ProjectKind.NodePackage, Ecosystem.Node, new Dictionary<string, string> { ["workspace"] = "mono" }),
            ["api"] = builder.GroupedProject("api", ProjectKind.NodePackage, Ecosystem.Node, new Dictionary<string, string> { ["workspace"] = "mono" }),
            ["Shop.Core"] = builder.GroupedProject("Shop.Core", ProjectKind.DotNetProject, Ecosystem.DotNet, null, "Shop.slnx"),
            ["Shop.Web"] = builder.GroupedProject("Shop.Web", ProjectKind.DotNetProject, Ecosystem.DotNet, null, "Shop.slnx", "Legacy.sln"),
            ["Tool"] = builder.GroupedProject("Tool", ProjectKind.DotNetProject, Ecosystem.DotNet),
            ["scripts"] = builder.GroupedProject("scripts", ProjectKind.PythonProject, Ecosystem.Python),
        };
        builder.ProjectReference(projects["api"], projects["Shop.Core"]);
        builder.ProjectReference(projects["Shop.Web"], projects["Shop.Core"]);
        builder.ProjectReference(projects["scripts"], projects["ui"]);
        return (builder.Build(), projects);
    }

    private GraphViewDto Project(Domain.Model.ArchitectureModel model, params string[] expanded) =>
        _projector.Project(model, new GraphQuery { RepositoryId = model.RepositoryId.ToString(), Level = GraphLevels.Repository, Expanded = expanded }).Value;

    private static string[] Edges(GraphViewDto view)
    {
        var labels = view.Nodes.ToDictionary(n => n.Id, n => n.Label);
        return [.. view.Edges.Select(e => $"{labels[e.Source]}->{labels[e.Target]}:{e.Count}").Order(StringComparer.Ordinal)];
    }

    [Fact]
    public void Projects_are_grouped_by_workspace_solution_and_ecosystem()
    {
        var (model, _) = Sample();

        var view = Project(model);

        Assert.Equal(GraphLevels.Repository, view.Level);
        Assert.Equal(
            [".NET projects (1 project)", "Legacy.sln (1 project)", "Python projects (1 project)", "Shop.slnx (1 project)", "mono (workspace) (3 projects)"],
            view.Nodes.Select(n => $"{n.Label} ({n.Sublabel})"));
        Assert.All(view.Nodes, n => Assert.Equal((GraphNodeKinds.Group, true, false), (n.Kind, n.IsExpandable, n.IsExpanded)));
        // Group-to-group edges aggregate the project dependencies.
        Assert.Equal(["Legacy.sln->Shop.slnx:1", "Python projects->mono (workspace):1", "mono (workspace)->Shop.slnx:1"], Edges(view));
    }

    [Fact]
    public void An_expanded_group_shows_its_projects_whose_edges_attach_to_them()
    {
        var (model, projects) = Sample();
        var initial = Project(model);
        var workspace = initial.Nodes.Single(n => n.Label == "mono (workspace)");

        var view = Project(model, workspace.Id);

        Assert.True(view.Nodes.Single(n => n.Id == workspace.Id).IsExpanded);
        Assert.Equal(["api", "mono", "ui"], view.Nodes.Where(n => n.ParentId == workspace.Id).Select(n => n.Label).Order(StringComparer.Ordinal));
        Assert.Equal(["Legacy.sln->Shop.slnx:1", "Python projects->ui:1", "api->Shop.slnx:1"], Edges(view));

        // Expanding a project inside the group works as at the project level.
        Assert.Equal(view.Nodes.Count, Project(model, workspace.Id, projects["api"].Id.ToString()).Nodes.Count); // api has no modules
    }

    [Fact]
    public void Collapsing_a_group_restores_the_identical_view()
    {
        var (model, _) = Sample();
        var initial = Project(model);
        var group = initial.Nodes.Single(n => n.Label == "Shop.slnx").Id;

        Project(model, group);
        var collapsed = Project(model);

        Assert.Equal(Edges(initial), Edges(collapsed));
        Assert.Equal(initial.Nodes.Select(n => n.Id), collapsed.Nodes.Select(n => n.Id));
    }

    [Fact]
    public void Group_expansions_respect_the_node_budget()
    {
        var (model, _) = Sample();
        var workspace = Project(model).Nodes.Single(n => n.Label == "mono (workspace)").Id;

        var view = _projector.Project(model, new GraphQuery
        {
            RepositoryId = model.RepositoryId.ToString(), Level = GraphLevels.Repository, Expanded = [workspace], NodeBudget = 6,
        }).Value;

        Assert.False(view.Nodes.Single(n => n.Id == workspace).IsExpanded);
        Assert.Equal(TruncationReasons.ExpansionRefused, view.Truncation!.Reason);
    }

    [Fact]
    public void The_neighborhood_of_a_project_inside_a_collapsed_group_focuses_the_group()
    {
        var (model, projects) = Sample();

        var view = _projector.Project(model, new GraphQuery
        {
            RepositoryId = model.RepositoryId.ToString(), Level = GraphLevels.Repository, Focus = projects["scripts"].Id.ToString(),
        }).Value;

        Assert.Equal(["Python projects", "mono (workspace)"], view.Nodes.Select(n => n.Label));
        Assert.Equal(["Repository", "Python projects"], view.Breadcrumbs.Select(b => b.Label));
    }

    [Fact]
    public void A_repository_without_projects_shows_its_folders()
    {
        var builder = new ModelBuilder();
        builder.LooseFile("README.md");
        builder.LooseFile("docs/guide.md");
        builder.LooseFile("docs/api/index.md");
        builder.LooseFile("scripts/build.sh");
        var model = builder.Build();
        var query = new GraphQuery { RepositoryId = model.RepositoryId.ToString() };

        var view = _projector.Project(model, query).Value;

        Assert.Equal(GraphLevels.Folder, view.Level);
        Assert.Equal(["docs 2 expandable", "scripts 1 leaf", "(repository root) 1 leaf"],
            view.Nodes.Select(n => $"{n.Label} {n.Metrics.Size} {(n.IsExpandable ? "expandable" : "leaf")}"));
        Assert.Empty(view.Edges);

        var docs = view.Nodes[0].Id;
        var expanded = _projector.Project(model, query with { Expanded = [docs] }).Value;
        Assert.Equal(["api"], expanded.Nodes.Where(n => n.ParentId == docs).Select(n => n.Label));
        Assert.Equal("docs/api", expanded.Nodes.Single(n => n.Label == "api").Sublabel);
    }
}
