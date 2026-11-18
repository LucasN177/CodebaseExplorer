using CodebaseExplorer.Analysis.FileSystem;
using CodebaseExplorer.Analysis.Pipeline;
using CodebaseExplorer.Analysis.ProjectSystems.Python;
using CodebaseExplorer.Analysis.Scanning;
using CodebaseExplorer.Analysis.Tests.ProjectSystems.Node;
using CodebaseExplorer.Application.Analysis;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Dependencies;
using CodebaseExplorer.Domain.Projects;
using CodebaseExplorer.Domain.Relationships;
using CodebaseExplorer.Domain.Runs;
using CodebaseExplorer.TestRepositories;

namespace CodebaseExplorer.Analysis.Tests.ProjectSystems.Python;

/// <summary>ANALYSIS-011 against the Python sections of the REPO-007 fixtures' EXPECTED.md.</summary>
public sealed class PythonFixtureTests
{
    private static readonly RepositoryId Repository = new(Guid.Parse("0193a1b2-c3d4-7e5f-8071-8293a4b5c6f2"));
    private static readonly AnalysisRunId Run = new(Guid.Parse("0193a1b2-c3d4-7e5f-8071-000000000111"));

    internal static async Task<RepositoryAnalysis> AnalyzeAsync(string root)
    {
        var reader = new RepositoryFileReader();
        var pipeline = new AnalysisPipeline(
            new RepositoryScanner(reader, TimeProvider.System), reader, [new PythonProjectSystemAnalyzer()], [], TimeProvider.System);
        var result = await pipeline.AnalyzeAsync(new AnalysisRequest(Repository, new RepositoryRoot(root), Run), null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        return result.Value;
    }

    [Fact]
    public async Task PythonSrcLayout_MatchesSections2To4()
    {
        var analysis = await AnalyzeAsync(TestRepositoryCatalog.GetFixturePath("python-src-layout"));
        var model = analysis.Model;

        var project = Assert.Single(model.Projects);
        Assert.Equal(
            ("Inventory-Service", ProjectKind.PythonProject, Ecosystem.Python, ".", "pyproject.toml", "python"),
            (project.Name, project.Kind, project.Ecosystem, project.RootDirectory.ToString(), model.FindFile(project.ProjectFileId!.Value)!.Path.Value, project.PrimaryLanguage.Value));
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["version"] = "0.3.0",
                ["requiresPython"] = ">=3.11",
                ["buildBackend"] = "hatchling.build",
                ["layout"] = "src",
                ["manifests"] = "pyproject.toml;requirements-dev.txt",
            },
            project.Properties.ToDictionary(p => p.Key, p => p.Value));

        Assert.Equal(
            [
                "PackageReference Inventory-Service -> PyYAML Confirmed pyproject.project.dependencies@pyproject.toml:11:5 {versionSpec=~=6.0}",
                "PackageReference Inventory-Service -> attrs Confirmed pyproject.project.dependencies@pyproject.toml:12:5 {}",
                "PackageReference Inventory-Service -> black Confirmed requirements.txt@requirements-dev.txt:3:1 {marker=python_version >= \"3.11\", versionSpec===24.8.0}",
                "PackageReference Inventory-Service -> pytest Confirmed pyproject.project.optional-dependencies@pyproject.toml:16:8, requirements.txt@requirements-dev.txt:2:1 {extra=dev, versionSpec=>=8}",
                "PackageReference Inventory-Service -> requests Confirmed pyproject.project.dependencies@pyproject.toml:10:5 {versionSpec=>=2.31}",
            ],
            NodeFixtureTests.Describe(model, RelationshipKind.ProjectReference, RelationshipKind.PackageReference));
        Assert.Equal(["attrs", "black", "pytest", "pyyaml", "requests"], model.ExternalDependencies.Select(d => d.Name).Order(StringComparer.Ordinal));
        Assert.All(model.ExternalDependencies, d => Assert.Equal(PackageEcosystem.PyPI, d.Ecosystem));

        Assert.Equal(
            [
                (DiagnosticSeverity.Info, PythonCodes.OptionIgnored, "requirements-dev.txt:4:1"),
                (DiagnosticSeverity.Warning, PythonCodes.IncludeOutsideRepository, "requirements-dev.txt:5:1"),
            ],
            analysis.Diagnostics.Select(d => (d.Severity, d.Code, NodeFixtureTests.Location(model, d.Location))).OrderBy(d => d.Item3, StringComparer.Ordinal));
    }

    [Fact]
    public async Task MixedPolyglot_FindsThePoetryProject()
    {
        var analysis = await AnalyzeAsync(TestRepositoryCatalog.GetFixturePath("mixed-polyglot"));
        var model = analysis.Model;

        var project = Assert.Single(model.Projects);
        Assert.Equal(("report-tool", "tools/py"), (project.Name, project.RootDirectory.Value));
        Assert.Equal(
            new Dictionary<string, string> { ["version"] = "1.0.0", ["requiresPython"] = "^3.12", ["layout"] = "flat", ["manifests"] = "pyproject.toml" },
            project.Properties.ToDictionary(p => p.Key, p => p.Value));
        Assert.Equal(
            [
                "PackageReference report-tool -> pandas Confirmed pyproject.poetry.dependencies@tools/py/pyproject.toml:7:1 {versionSpec=^2.2}",
                "PackageReference report-tool -> pytest Confirmed pyproject.poetry.group-dependencies@tools/py/pyproject.toml:10:1 {devOnly=true, group=dev, versionSpec=^8.3}",
            ],
            NodeFixtureTests.Describe(model, RelationshipKind.PackageReference));
        Assert.Empty(analysis.Diagnostics);
    }
}
