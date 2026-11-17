using System.Xml.Linq;
using CodebaseExplorer.TestRepositories;

namespace CodebaseExplorer.IntegrationTests.Architecture;

/// <summary>
/// Enforces the dependency rules from docs/architecture/SYSTEM_ARCHITECTURE.md ("Dependency rules")
/// by reading the ProjectReference items of every production project.
/// If one of these tests fails, the architecture was changed: write or update an ADR first.
/// </summary>
public sealed class ProjectReferenceRulesTests
{
    private static readonly Dictionary<string, string[]> AllowedReferences = new()
    {
        ["CodebaseExplorer.Domain"] = [],
        ["CodebaseExplorer.Contracts"] = [],
        ["CodebaseExplorer.Application"] = ["CodebaseExplorer.Domain", "CodebaseExplorer.Contracts"],
        ["CodebaseExplorer.Infrastructure"] = ["CodebaseExplorer.Application", "CodebaseExplorer.Domain"],
        ["CodebaseExplorer.Storage"] = ["CodebaseExplorer.Application", "CodebaseExplorer.Domain"],
        ["CodebaseExplorer.Analysis"] = ["CodebaseExplorer.Application", "CodebaseExplorer.Domain"],
        ["CodebaseExplorer.Graph"] = ["CodebaseExplorer.Application", "CodebaseExplorer.Domain", "CodebaseExplorer.Contracts"],
        ["CodebaseExplorer.Git"] = ["CodebaseExplorer.Application", "CodebaseExplorer.Domain"],
        ["CodebaseExplorer.AI"] = ["CodebaseExplorer.Application", "CodebaseExplorer.Domain", "CodebaseExplorer.Contracts"],
        ["CodebaseExplorer.Desktop"] =
        [
            "CodebaseExplorer.Application", "CodebaseExplorer.Contracts", "CodebaseExplorer.Domain",
            "CodebaseExplorer.Infrastructure", "CodebaseExplorer.Storage", "CodebaseExplorer.Analysis",
            "CodebaseExplorer.Graph", "CodebaseExplorer.Git", "CodebaseExplorer.AI",
        ],
    };

    public static TheoryData<string> ProductionProjects => [.. AllowedReferences.Keys];

    [Theory]
    [MemberData(nameof(ProductionProjects))]
    public void Project_OnlyReferencesAllowedProjects(string projectName)
    {
        var actual = ReadProjectReferences(projectName);

        var forbidden = actual.Except(AllowedReferences[projectName], StringComparer.Ordinal).ToArray();

        Assert.True(
            forbidden.Length == 0,
            $"{projectName} references {string.Join(", ", forbidden)}, which violates the dependency rules.");
    }

    [Fact]
    public void SourceFolder_ContainsOnlyKnownProjects()
    {
        var sourceProjects = Directory
            .EnumerateFiles(Path.Combine(TestRepositoryCatalog.RepositoryRoot, "src"), "*.csproj", SearchOption.AllDirectories)
            .Select(Path.GetFileNameWithoutExtension)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(AllowedReferences.Keys.Order(StringComparer.Ordinal), sourceProjects);
    }

    [Theory]
    [InlineData("CodebaseExplorer.Domain")]
    [InlineData("CodebaseExplorer.Contracts")]
    public void InnerLayer_HasNoPackageReferences(string projectName)
    {
        var packages = LoadProject(projectName)
            .Descendants("PackageReference")
            .Select(e => (string?)e.Attribute("Include"))
            .ToArray();

        Assert.Empty(packages);
    }

    private static string[] ReadProjectReferences(string projectName) =>
        [.. LoadProject(projectName)
            .Descendants("ProjectReference")
            .Select(e => (string?)e.Attribute("Include") ?? string.Empty)
            .Select(include => Path.GetFileNameWithoutExtension(include.Replace('\\', '/')))];

    private static XDocument LoadProject(string projectName)
    {
        var path = Path.Combine(TestRepositoryCatalog.RepositoryRoot, "src", projectName, $"{projectName}.csproj");
        return XDocument.Load(path);
    }
}
