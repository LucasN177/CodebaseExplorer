using CodebaseExplorer.Analysis.FileSystem;
using CodebaseExplorer.Analysis.Scanning;
using CodebaseExplorer.Application.Analysis;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Domain.Files;
using CodebaseExplorer.TestRepositories;

namespace CodebaseExplorer.Analysis.Tests.Fixtures;

/// <summary>Checks the scan of fixture <c>dotnet-layered</c> against section 1 of its EXPECTED.md.</summary>
public sealed class DotnetLayeredFixtureTests
{
    private const string Fixture = "dotnet-layered";

    private static Task<ScanResult> ScanAsync() =>
        new RepositoryScanner(new RepositoryFileReader(), TimeProvider.System).ScanAsync(
            new RepositoryRoot(TestRepositoryCatalog.GetFixturePath(Fixture)),
            AnalysisLimits.Default,
            progress: null,
            TestContext.Current.CancellationToken);

    [Fact]
    public async Task Scan_FindsExpectedMarkersAndClassifications()
    {
        var result = await ScanAsync();
        var markers = result.Files
            .Where(f => f.Marker is not null)
            .ToDictionary(f => f.Path.Value, f => (f.Marker!.Value.ToString(), f.Classification));

        Assert.Equal(
            new Dictionary<string, (string, FileClassification)>
            {
                ["Shop.sln"] = ("DotNetSolution", FileClassification.SolutionFile),
                ["Shop.slnx"] = ("DotNetSolutionXml", FileClassification.SolutionFile),
                ["src/Shop.Domain/Shop.Domain.csproj"] = ("DotNetProject", FileClassification.ProjectManifest),
                ["src/Shop.Application/Shop.Application.csproj"] = ("DotNetProject", FileClassification.ProjectManifest),
                ["src/Shop.Infrastructure/Shop.Infrastructure.csproj"] = ("DotNetProject", FileClassification.ProjectManifest),
                ["src/Shop.Web/Shop.Web.csproj"] = ("DotNetProject", FileClassification.ProjectManifest),
                ["tests/Shop.Tests/Shop.Tests.csproj"] = ("DotNetProject", FileClassification.ProjectManifest),
            },
            markers);

        var byPath = result.Files.ToDictionary(f => f.Path.Value);
        Assert.Equal(FileClassification.Configuration, byPath["Directory.Build.props"].Classification);
        Assert.Equal(FileClassification.Configuration, byPath["Directory.Packages.props"].Classification);
        Assert.Equal(FileClassification.Documentation, byPath["EXPECTED.md"].Classification);
        Assert.Equal(FileClassification.Source, byPath["src/Shop.Domain/Orders/Order.cs"].Classification);
        Assert.Equal(FileClassification.Test, byPath["tests/Shop.Tests/PlaceOrderHandlerTests.cs"].Classification);
        Assert.DoesNotContain(result.Files, f => f.Path.Value.Contains("Shop.Legacy", StringComparison.Ordinal));
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void FixtureFiles_UseLfLineEndings_SoDocumentedLocationsStayValid()
    {
        var root = TestRepositoryCatalog.GetFixturePath(Fixture);

        var withCarriageReturn = Directory
            .EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => File.ReadAllBytes(path).Contains((byte)'\r'))
            .Select(path => Path.GetRelativePath(root, path))
            .ToArray();

        Assert.Empty(withCarriageReturn);
    }
}
