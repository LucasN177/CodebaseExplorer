using CodebaseExplorer.Analysis.FileSystem;
using CodebaseExplorer.Analysis.Pipeline;
using CodebaseExplorer.Analysis.ProjectSystems.Node;
using CodebaseExplorer.Analysis.Scanning;
using CodebaseExplorer.Application.Analysis;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Evidence;
using CodebaseExplorer.Domain.Model;
using CodebaseExplorer.Domain.Projects;
using CodebaseExplorer.Domain.Relationships;
using CodebaseExplorer.Domain.Runs;
using CodebaseExplorer.TestRepositories;

namespace CodebaseExplorer.Analysis.Tests.ProjectSystems.Node;

/// <summary>ANALYSIS-009 against the Node sections of the REPO-007 fixtures' EXPECTED.md.</summary>
public sealed class NodeFixtureTests
{
    private static readonly RepositoryId Repository = new(Guid.Parse("0193a1b2-c3d4-7e5f-8071-8293a4b5c6e1"));
    private static readonly AnalysisRunId Run = new(Guid.Parse("0193a1b2-c3d4-7e5f-8071-000000000091"));

    internal static async Task<RepositoryAnalysis> AnalyzeAsync(string root)
    {
        var reader = new RepositoryFileReader();
        var pipeline = new AnalysisPipeline(
            new RepositoryScanner(reader, TimeProvider.System), reader, [new NodeProjectSystemAnalyzer()], [], TimeProvider.System);
        var result = await pipeline.AnalyzeAsync(new AnalysisRequest(Repository, new RepositoryRoot(root), Run), null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        return result.Value;
    }

    internal static string Location(ArchitectureModel model, SourceLocation? location) =>
        location is not { } l ? "-" : $"{model.FindFile(l.FileId)!.Path}:{l.StartLine}:{l.StartColumn}";

    private static string NameOf(ArchitectureModel model, ElementReference reference) => reference.Kind == ElementKind.Project
        ? model.FindProject(reference.Id)!.Name
        : model.FindExternalDependency(reference.Id)!.DisplayName;

    /// <summary>One line per relationship: kind, source, target, level, evidence (rule@location), attributes.</summary>
    internal static string[] Describe(ArchitectureModel model, params RelationshipKind[] kinds) =>
        [.. model.Relationships
            .Where(r => kinds.Contains(r.Kind))
            .Select(r => $"{r.Kind} {NameOf(model, r.Source)} -> {NameOf(model, r.Target)} {r.Level} "
                + string.Join(", ", r.Evidence.Select(e => $"{e.Rule}@{Location(model, e.Location)}").Order(StringComparer.Ordinal))
                + " {" + string.Join(", ", r.Attributes.Select(a => $"{a.Key}={a.Value}")) + "}")
            .Order(StringComparer.Ordinal)];

    private static Dictionary<string, string> Properties(CodeProject project) => project.Properties.ToDictionary(p => p.Key, p => p.Value);

    [Fact]
    public async Task TsMonorepo_ProjectsMatchSection2()
    {
        var model = (await AnalyzeAsync(TestRepositoryCatalog.GetFixturePath("ts-monorepo-workspaces"))).Model;

        var projects = model.Projects.OrderBy(p => p.RootDirectory.Value, StringComparer.Ordinal).ToArray();
        Assert.Equal(
            ["acme-monorepo NodeWorkspaceRoot . package.json typescript", "web NodePackage apps/web apps/web/package.json typescript",
             "@acme/core NodePackage packages/core packages/core/package.json typescript", "@acme/shared NodePackage packages/shared packages/shared/package.json typescript"],
            projects.Select(p => $"{p.Name} {p.Kind} {p.RootDirectory} {model.FindFile(p.ProjectFileId!.Value)!.Path} {p.PrimaryLanguage}"));
        Assert.All(projects, p => Assert.Equal(Ecosystem.Node, p.Ecosystem));

        Assert.Equal(
            new Dictionary<string, string>
            {
                ["private"] = "true",
                ["workspaces"] = "packages/*;apps/*",
                ["scripts"] = "build;postinstall",
                ["lifecycleScripts"] = "postinstall",
                ["tsconfig"] = "tsconfig.json",
            },
            Properties(projects[0]));
        Assert.Equal(
            new Dictionary<string, string> { ["private"] = "true", ["workspace"] = "acme-monorepo", ["tsconfig"] = "apps/web/tsconfig.json" },
            Properties(projects[1]));
        Assert.Equal(
            new Dictionary<string, string> { ["version"] = "1.2.0", ["workspace"] = "acme-monorepo", ["tsconfig"] = "packages/core/tsconfig.json" },
            Properties(projects[2]));
        Assert.Equal(new Dictionary<string, string> { ["version"] = "0.1.0", ["workspace"] = "acme-monorepo" }, Properties(projects[3]));

        var root = projects[0];
        Assert.Equal(
            ["EXPECTED.md", "package.json", "tsconfig.base.json", "tsconfig.json"],
            model.Files.Where(f => f.ProjectId == root.Id).Select(f => f.Path.Value).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task TsMonorepo_RelationshipsAndDiagnosticsMatchSections3To5()
    {
        var analysis = await AnalyzeAsync(TestRepositoryCatalog.GetFixturePath("ts-monorepo-workspaces"));
        var model = analysis.Model;

        Assert.Equal(
            [
                "PackageReference @acme/core -> lodash Confirmed package.json.dependencies@packages/core/package.json:7:5 {dependencyType=dependencies, versionSpec=^4.17.21}",
                "PackageReference acme-monorepo -> typescript Confirmed package.json.devDependencies@package.json:13:5 {dependencyType=devDependencies, devOnly=true, versionSpec=^5.6.0}",
                "PackageReference web -> react Confirmed package.json.dependencies@apps/web/package.json:6:5 {dependencyType=dependencies, versionSpec=^19.0.0}",
                "PackageReference web -> vitest Confirmed package.json.devDependencies@apps/web/package.json:9:5 {dependencyType=devDependencies, devOnly=true, versionSpec=^3.0.0}",
                "ProjectReference @acme/core -> @acme/shared Confirmed package.json.dependencies@packages/core/package.json:6:5, tsconfig.references@packages/core/tsconfig.json:6:20 {dependencyType=dependencies, versionSpec=workspace:*}",
                "ProjectReference web -> @acme/core Confirmed package.json.dependencies@apps/web/package.json:5:5 {dependencyType=dependencies, versionSpec=^1.0.0}",
            ],
            Describe(model, RelationshipKind.ProjectReference, RelationshipKind.PackageReference));
        Assert.Equal(["lodash", "react", "typescript", "vitest"], model.ExternalDependencies.Select(d => d.Name).Order(StringComparer.Ordinal));
        Assert.All(model.ExternalDependencies, d => Assert.Equal(Domain.Dependencies.PackageEcosystem.Npm, d.Ecosystem));

        var diagnostic = Assert.Single(analysis.Diagnostics);
        Assert.Equal((DiagnosticSeverity.Info, NodeCodes.LifecycleNotExecuted, "package.json:10:5"),
            (diagnostic.Severity, diagnostic.Code, Location(model, diagnostic.Location)));
    }

    [Fact]
    public async Task JsCommonJs_MatchesSections2And3()
    {
        var analysis = await AnalyzeAsync(TestRepositoryCatalog.GetFixturePath("js-commonjs"));
        var model = analysis.Model;

        var project = Assert.Single(model.Projects);
        Assert.Equal(("legacy-tool", ProjectKind.NodePackage, ".", "javascript"), (project.Name, project.Kind, project.RootDirectory.ToString(), project.PrimaryLanguage.Value));
        Assert.Equal(new Dictionary<string, string> { ["version"] = "2.0.0", ["scripts"] = "test" }, Properties(project));
        Assert.Equal(
            [
                "PackageReference legacy-tool -> express Confirmed package.json.dependencies@package.json:9:5 {dependencyType=dependencies, versionSpec=^4.19.0}",
                "PackageReference legacy-tool -> mocha Confirmed package.json.devDependencies@package.json:12:5 {dependencyType=devDependencies, devOnly=true, versionSpec=^10.0.0}",
            ],
            Describe(model, RelationshipKind.ProjectReference, RelationshipKind.PackageReference));
        Assert.Empty(analysis.Diagnostics);
    }

    [Fact]
    public async Task MixedPolyglot_FindsTheFrontendPackage()
    {
        var analysis = await AnalyzeAsync(TestRepositoryCatalog.GetFixturePath("mixed-polyglot"));
        var model = analysis.Model;

        var project = Assert.Single(model.Projects);
        Assert.Equal(("frontend", "frontend", "typescript"), (project.Name, project.RootDirectory.Value, project.PrimaryLanguage.Value));
        Assert.Equal(new Dictionary<string, string> { ["private"] = "true", ["tsconfig"] = "frontend/tsconfig.json" }, Properties(project));
        Assert.Equal(
            ["PackageReference frontend -> axios Confirmed package.json.dependencies@frontend/package.json:5:5 {dependencyType=dependencies, versionSpec=^1.7.0}"],
            Describe(model, RelationshipKind.PackageReference));
        Assert.Empty(analysis.Diagnostics);
    }

    [Fact]
    public async Task Analysis_IsDeterministic()
    {
        var root = TestRepositoryCatalog.GetFixturePath("ts-monorepo-workspaces");

        var first = await AnalyzeAsync(root);
        var second = await AnalyzeAsync(root);

        Assert.Equal(Describe(first.Model, Enum.GetValues<RelationshipKind>()), Describe(second.Model, Enum.GetValues<RelationshipKind>()));
        Assert.Equal(first.Model.Projects.Select(p => p.Id), second.Model.Projects.Select(p => p.Id));
    }
}
