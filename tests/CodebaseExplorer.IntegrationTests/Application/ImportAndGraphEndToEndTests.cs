using CodebaseExplorer.Application.Graph;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Contracts.Graph;
using CodebaseExplorer.TestRepositories;
using Microsoft.Extensions.DependencyInjection;

namespace CodebaseExplorer.IntegrationTests.Application;

/// <summary>
/// CORE-006 acceptance: fixture <c>dotnet-layered</c> → import → project-level graph matching EXPECTED.md,
/// using the real composition root (all modules wired as in the application).
/// </summary>
public sealed class ImportAndGraphEndToEndTests : IAsyncLifetime
{
    private readonly TestApplication _app = new();

    public ValueTask InitializeAsync() => _app.InitializeAsync();

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    [Fact]
    public async Task ImportFixture_ThenProjectGraph_MatchesExpected()
    {
        var import = _app.Services.GetRequiredService<ImportLocalRepository>();
        var getGraph = _app.Services.GetRequiredService<GetGraphView>();

        var summary = (await import.ExecuteAsync(
            TestRepositoryCatalog.GetFixturePath("dotnet-layered"), progress: null, TestContext.Current.CancellationToken)).Value;

        Assert.Equal("dotnet-layered", summary.Name);
        Assert.Equal("succeeded", summary.Status); // warnings do not make a run partial
        Assert.Equal(6, summary.ProjectCount);
        Assert.Equal(13, summary.RelationshipCount);
        Assert.Equal(1, summary.WarningCount);
        Assert.Equal(0, summary.ErrorCount);
        var warning = Assert.Single(summary.Diagnostics);
        Assert.Equal(("warning", "src/Shop.Infrastructure/Shop.Infrastructure.csproj", 6), (warning.Severity, warning.Path, warning.Line));

        var view = (await getGraph.ExecuteAsync(
            new GraphQuery { RepositoryId = summary.RepositoryId }, TestContext.Current.CancellationToken)).Value;

        Assert.Equal(
            ["Shop.Application", "Shop.Domain", "Shop.Infrastructure", "Shop.Legacy", "Shop.Tests", "Shop.Web"],
            view.Nodes.Select(n => n.Label));
        Assert.Equal([GraphBadges.Missing], view.Nodes.Single(n => n.Label == "Shop.Legacy").Badges);

        var labels = view.Nodes.ToDictionary(n => n.Id, n => n.Label);
        var edges = view.Edges
            .Select(e => (labels[e.Source], labels[e.Target], e.MaxLevel))
            .OrderBy(e => e.Item1, StringComparer.Ordinal).ThenBy(e => e.Item2, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                ("Shop.Application", "Shop.Domain", GraphEvidenceLevels.Confirmed),
                ("Shop.Infrastructure", "Shop.Application", GraphEvidenceLevels.Confirmed),
                ("Shop.Infrastructure", "Shop.Domain", GraphEvidenceLevels.Confirmed),
                ("Shop.Infrastructure", "Shop.Legacy", GraphEvidenceLevels.Syntactic),
                ("Shop.Tests", "Shop.Application", GraphEvidenceLevels.Confirmed),
                ("Shop.Tests", "Shop.Domain", GraphEvidenceLevels.Confirmed),
                ("Shop.Web", "Shop.Application", GraphEvidenceLevels.Confirmed),
                ("Shop.Web", "Shop.Infrastructure", GraphEvidenceLevels.Confirmed),
            ],
            edges);
        Assert.All(view.Edges, e => Assert.True(e.IsDeclared));
    }

    [Fact]
    public async Task ImportFixture_WithExternalDependencies_ShowsPackages()
    {
        var summary = (await _app.Services.GetRequiredService<ImportLocalRepository>().ExecuteAsync(
            TestRepositoryCatalog.GetFixturePath("dotnet-layered"), null, TestContext.Current.CancellationToken)).Value;

        var view = (await _app.Services.GetRequiredService<GetGraphView>().ExecuteAsync(
            new GraphQuery { RepositoryId = summary.RepositoryId, IncludeExternalDependencies = true },
            TestContext.Current.CancellationToken)).Value;

        Assert.Equal(
            ["Microsoft.AspNetCore.App", "Microsoft.EntityFrameworkCore.Sqlite", "Microsoft.Extensions.DependencyInjection.Abstractions", "NSubstitute", "xunit.v3"],
            view.Nodes.Where(n => n.Kind == GraphNodeKinds.ExternalDependency).Select(n => n.Label));
        Assert.Equal(13, view.Edges.Count);
    }
}
