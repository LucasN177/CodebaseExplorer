using CodebaseExplorer.Analysis.ProjectSystems.Node;
using CodebaseExplorer.Domain.Projects;
using CodebaseExplorer.Domain.Relationships;
using CodebaseExplorer.Domain.Runs;
using CodebaseExplorer.TestRepositories;

namespace CodebaseExplorer.Analysis.Tests.ProjectSystems.Node;

/// <summary>ANALYSIS-009 edge cases with temporary repositories.</summary>
public sealed class NodeProjectSystemAnalyzerTests : IDisposable
{
    private readonly TempDirectory _repository = new();

    public void Dispose() => _repository.Dispose();

    [Fact]
    public async Task Pnpm_workspace_marks_the_root_and_its_members()
    {
        _repository.WriteText("package.json", "{ \"name\": \"mono\", \"private\": true }");
        _repository.WriteText("pnpm-workspace.yaml", "packages:\n  - 'libs/*'\n  - '!libs/old'\n");
        _repository.WriteText("libs/a/package.json", "{ \"name\": \"@m/a\", \"dependencies\": { \"@m/b\": \"workspace:^\" } }");
        _repository.WriteText("libs/b/package.json", "{ \"name\": \"@m/b\" }");
        _repository.WriteText("libs/old/package.json", "{ \"name\": \"@m/old\" }");

        var model = (await NodeFixtureTests.AnalyzeAsync(_repository.FullPath)).Model;

        var root = model.Projects.Single(p => p.Name == "mono");
        Assert.Equal(ProjectKind.NodeWorkspaceRoot, root.Kind);
        Assert.Equal("libs/*;!libs/old", root.Properties["workspaces"]);
        Assert.Equal("mono", model.Projects.Single(p => p.Name == "@m/a").Properties["workspace"]);
        Assert.False(model.Projects.Single(p => p.Name == "@m/old").Properties.ContainsKey("workspace"));
        Assert.Contains("ProjectReference @m/a -> @m/b Confirmed package.json.dependencies@libs/a/package.json:1:37 {dependencyType=dependencies, versionSpec=workspace:^}",
            NodeFixtureTests.Describe(model, RelationshipKind.ProjectReference));
    }

    [Fact]
    public async Task Yarn_workspaces_object_form_is_supported()
    {
        _repository.WriteText("package.json", "{ \"name\": \"y\", \"workspaces\": { \"packages\": [\"p/*\"], \"nohoist\": [] } }");
        _repository.WriteText("p/x/package.json", "{ \"name\": \"x\" }");

        var model = (await NodeFixtureTests.AnalyzeAsync(_repository.FullPath)).Model;

        Assert.Equal("y", model.Projects.Single(p => p.Name == "x").Properties["workspace"]);
    }

    [Fact]
    public async Task Invalid_and_too_deep_manifests_still_yield_a_project_with_a_warning()
    {
        _repository.WriteText("broken/package.json", "{ \"name\": ");
        _repository.WriteText("deep/package.json", "{\"a\":" + new string('[', 5_000) + new string(']', 5_000) + "}");

        var analysis = await NodeFixtureTests.AnalyzeAsync(_repository.FullPath);

        Assert.Equal(["broken", "deep"], analysis.Model.Projects.Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal(2, analysis.Diagnostics.Count(d => d is { Code: NodeCodes.ManifestInvalid, Severity: DiagnosticSeverity.Warning }));
        Assert.Empty(analysis.Model.Relationships);
    }

    [Fact]
    public async Task Manifests_above_the_size_limit_are_not_parsed()
    {
        _repository.WriteText("package.json", "{ \"name\": \"big\", \"description\": \"" + new string('x', 1_100_000) + "\" }");

        var analysis = await NodeFixtureTests.AnalyzeAsync(_repository.FullPath);

        Assert.Equal(NodeCodes.ManifestInvalid, Assert.Single(analysis.Diagnostics).Code);
        Assert.NotEqual("big", Assert.Single(analysis.Model.Projects).Name);
    }

    [Fact]
    public async Task Missing_name_falls_back_to_the_folder_and_non_string_versions_are_skipped()
    {
        _repository.WriteText("tool/package.json", "{ \"dependencies\": { \"ok\": \"1.0.0\", \"bad\": { \"version\": \"1\" } } }");

        var analysis = await NodeFixtureTests.AnalyzeAsync(_repository.FullPath);

        Assert.Equal("tool", Assert.Single(analysis.Model.Projects).Name);
        Assert.Equal(["ok"], analysis.Model.ExternalDependencies.Select(d => d.Name));
        Assert.Equal([NodeCodes.ManifestEntryIgnored, NodeCodes.NameMissing], analysis.Diagnostics.Select(d => d.Code).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Duplicate_package_names_resolve_to_the_first_and_warn()
    {
        _repository.WriteText("a/package.json", "{ \"name\": \"same\" }");
        _repository.WriteText("b/package.json", "{ \"name\": \"same\" }");
        _repository.WriteText("c/package.json", "{ \"name\": \"c\", \"dependencies\": { \"same\": \"*\" } }");

        var analysis = await NodeFixtureTests.AnalyzeAsync(_repository.FullPath);

        Assert.Equal(NodeCodes.DuplicateName, Assert.Single(analysis.Diagnostics).Code);
        var reference = Assert.Single(analysis.Model.Relationships);
        Assert.Equal("a", analysis.Model.FindProject(reference.Target.Id)!.RootDirectory.Value);
    }

    [Fact]
    public async Task Unresolved_tsconfig_references_and_self_dependencies_are_handled()
    {
        _repository.WriteText("app/package.json", "{ \"name\": \"app\", \"devDependencies\": { \"app\": \"1\" } }");
        _repository.WriteText("app/tsconfig.json", "{ \"references\": [{ \"path\": \"../nowhere\" }, { \"path\": \"./tsconfig.json\" }] }");

        var analysis = await NodeFixtureTests.AnalyzeAsync(_repository.FullPath);

        Assert.Empty(analysis.Model.Relationships);
        var diagnostic = Assert.Single(analysis.Diagnostics);
        Assert.Equal((NodeCodes.TsConfigReferenceUnresolved, "app/tsconfig.json:1:20"),
            (diagnostic.Code, NodeFixtureTests.Location(analysis.Model, diagnostic.Location)));
    }

    [Fact]
    public async Task Scripts_are_listed_never_run_and_lifecycle_scripts_are_reported()
    {
        _repository.WriteText("package.json",
            "{ \"name\": \"s\", \"scripts\": { \"preinstall\": \"node evil.js\", \"prepare\": \"husky\", \"lint\": \"eslint .\" } }");

        var analysis = await NodeFixtureTests.AnalyzeAsync(_repository.FullPath);

        var project = Assert.Single(analysis.Model.Projects);
        Assert.Equal("lint;preinstall;prepare", project.Properties["scripts"]);
        Assert.Equal("preinstall;prepare", project.Properties["lifecycleScripts"]);
        Assert.Equal(2, analysis.Diagnostics.Count(d => d.Code == NodeCodes.LifecycleNotExecuted));
    }
}
