using CodebaseExplorer.Analysis.FileSystem;
using CodebaseExplorer.Analysis.Scanning;
using CodebaseExplorer.Application.Analysis;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Domain.Files;
using CodebaseExplorer.TestRepositories;

namespace CodebaseExplorer.Analysis.Tests.Fixtures;

/// <summary>
/// REPO-007: the polyglot fixtures match section 1 (files, markers, classifications) of their EXPECTED.md.
/// The analyzer sections are asserted by ANALYSIS-009 … ANALYSIS-012.
/// </summary>
public sealed class PolyglotFixtureTests
{
    public static TheoryData<string> Fixtures() => ["ts-monorepo-workspaces", "js-commonjs", "python-src-layout", "mixed-polyglot"];

    private static async Task<ScanResult> ScanAsync(string fixture) =>
        await new RepositoryScanner(new RepositoryFileReader(), TimeProvider.System).ScanAsync(
            new RepositoryRoot(TestRepositoryCatalog.GetFixturePath(fixture)), AnalysisLimits.Default, progress: null, TestContext.Current.CancellationToken);

    private static Dictionary<string, (string Marker, FileClassification Classification)> Markers(ScanResult scan) =>
        scan.Files.Where(f => f.Marker is not null).ToDictionary(f => f.Path.Value, f => (f.Marker!.Value.ToString(), f.Classification));

    private static string[] PathsOf(ScanResult scan, FileClassification classification) =>
        [.. scan.Files.Where(f => f.Classification == classification).Select(f => f.Path.Value).Order(StringComparer.Ordinal)];

    [Fact]
    public async Task TsMonorepo_MatchesSection1()
    {
        var scan = await ScanAsync("ts-monorepo-workspaces");

        Assert.Equal(19, scan.Files.Count);
        Assert.Equal(
            new Dictionary<string, (string, FileClassification)>
            {
                ["apps/web/package.json"] = ("NodePackage", FileClassification.ProjectManifest),
                ["package.json"] = ("NodePackage", FileClassification.ProjectManifest),
                ["packages/core/package.json"] = ("NodePackage", FileClassification.ProjectManifest),
                ["packages/shared/package.json"] = ("NodePackage", FileClassification.ProjectManifest),
            },
            Markers(scan));
        Assert.Equal(["apps/web/tsconfig.json", "packages/core/tsconfig.json", "tsconfig.base.json", "tsconfig.json"], PathsOf(scan, FileClassification.Configuration));
        Assert.Equal(["apps/web/src/App.test.tsx"], PathsOf(scan, FileClassification.Test));
        Assert.Equal(9, PathsOf(scan, FileClassification.Source).Length);
        Assert.Empty(scan.Diagnostics);
    }

    [Fact]
    public async Task JsCommonJs_MatchesSection1()
    {
        var scan = await ScanAsync("js-commonjs");

        Assert.Equal(7, scan.Files.Count);
        Assert.Equal(new Dictionary<string, (string, FileClassification)> { ["package.json"] = ("NodePackage", FileClassification.ProjectManifest) }, Markers(scan));
        Assert.Equal(["lib/config.json"], PathsOf(scan, FileClassification.Configuration));
        Assert.Equal(["lib/index.js", "lib/routes/index.js", "lib/util/helper.js"], PathsOf(scan, FileClassification.Source));
        Assert.Equal(["test/index.test.js"], PathsOf(scan, FileClassification.Test));
    }

    [Fact]
    public async Task PythonSrcLayout_MatchesSection1()
    {
        var scan = await ScanAsync("python-src-layout");

        Assert.Equal(9, scan.Files.Count);
        Assert.Equal(
            new Dictionary<string, (string, FileClassification)>
            {
                ["pyproject.toml"] = ("PythonProject", FileClassification.ProjectManifest),
                ["requirements-dev.txt"] = ("PythonRequirements", FileClassification.ProjectManifest),
            },
            Markers(scan));
        Assert.Equal(5, PathsOf(scan, FileClassification.Source).Length);
        Assert.Equal(["tests/test_service.py"], PathsOf(scan, FileClassification.Test));
    }

    [Fact]
    public async Task MixedPolyglot_MatchesSection1()
    {
        var scan = await ScanAsync("mixed-polyglot");

        Assert.Equal(10, scan.Files.Count);
        Assert.Equal(
            new Dictionary<string, (string, FileClassification)>
            {
                ["backend/Api/Api.csproj"] = ("DotNetProject", FileClassification.ProjectManifest),
                ["frontend/package.json"] = ("NodePackage", FileClassification.ProjectManifest),
                ["tools/py/pyproject.toml"] = ("PythonProject", FileClassification.ProjectManifest),
            },
            Markers(scan));
        Assert.Equal(["frontend/tsconfig.json"], PathsOf(scan, FileClassification.Configuration));
        Assert.Equal(4, PathsOf(scan, FileClassification.Source).Length);
        Assert.Equal(["EXPECTED.md", "README.md"], PathsOf(scan, FileClassification.Documentation));
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void FixtureFiles_UseLfLineEndings_SoDocumentedLocationsStayValid(string fixture)
    {
        var root = TestRepositoryCatalog.GetFixturePath(fixture);
        var withCarriageReturn = Directory
            .EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => File.ReadAllBytes(path).Contains((byte)'\r'))
            .Select(path => Path.GetRelativePath(root, path))
            .ToList();

        Assert.Empty(withCarriageReturn);
    }
}
