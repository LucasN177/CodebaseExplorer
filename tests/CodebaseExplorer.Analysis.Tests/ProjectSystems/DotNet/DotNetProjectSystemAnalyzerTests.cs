using CodebaseExplorer.Analysis.FileSystem;
using CodebaseExplorer.Analysis.Pipeline;
using CodebaseExplorer.Analysis.ProjectSystems.DotNet;
using CodebaseExplorer.Analysis.Scanning;
using CodebaseExplorer.Application.Analysis;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Dependencies;
using CodebaseExplorer.Domain.Evidence;
using CodebaseExplorer.Domain.Model;
using CodebaseExplorer.Domain.Projects;
using CodebaseExplorer.Domain.Relationships;
using CodebaseExplorer.Domain.Runs;
using CodebaseExplorer.TestRepositories;

namespace CodebaseExplorer.Analysis.Tests.ProjectSystems.DotNet;

/// <summary>End-to-end pipeline over fixture <c>dotnet-layered</c>, checked against its EXPECTED.md.</summary>
public sealed class DotNetProjectSystemAnalyzerTests
{
    private static readonly RepositoryId Repository = new(Guid.Parse("0193a1b2-c3d4-7e5f-8071-8293a4b5c6d7"));
    private static readonly AnalysisRunId Run = new(Guid.Parse("0193a1b2-c3d4-7e5f-8071-000000000042"));

    private static async Task<RepositoryAnalysis> AnalyzeAsync(string root)
    {
        var reader = new RepositoryFileReader();
        var pipeline = new AnalysisPipeline(
            new RepositoryScanner(reader, TimeProvider.System), reader, [new DotNetProjectSystemAnalyzer()], [], TimeProvider.System);

        var result = await pipeline.AnalyzeAsync(
            new AnalysisRequest(Repository, new RepositoryRoot(root), Run), null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        return result.Value;
    }

    private static Task<RepositoryAnalysis> AnalyzeFixtureAsync() =>
        AnalyzeAsync(TestRepositoryCatalog.GetFixturePath("dotnet-layered"));

    private static string Name(ArchitectureModel model, ElementReference reference) => reference.Kind switch
    {
        ElementKind.Project => model.FindProject(reference.Id)!.Name,
        ElementKind.ExternalDependency => model.FindExternalDependency(reference.Id)!.Name,
        _ => reference.ToString(),
    };

    private static string Location(ArchitectureModel model, AnalysisEvidence evidence) =>
        $"{model.FindFile(evidence.Location!.Value.FileId)!.Path}:{evidence.Location.Value.StartLine}:{evidence.Location.Value.StartColumn}";

    [Fact]
    public async Task Projects_MatchExpectedSection3()
    {
        var model = (await AnalyzeFixtureAsync()).Model;
        var real = model.Projects.Where(p => !p.IsPlaceholder()).ToDictionary(p => p.Name);

        Assert.Equal(["Shop.Application", "Shop.Domain", "Shop.Infrastructure", "Shop.Tests", "Shop.Web"], real.Keys.Order());
        Assert.All(real.Values, p =>
        {
            Assert.Equal(ProjectKind.DotNetProject, p.Kind);
            Assert.Equal(Ecosystem.DotNet, p.Ecosystem);
            Assert.Equal("csharp", p.PrimaryLanguage.Value);
            Assert.Equal("Microsoft.NET.Sdk", p.Properties["Sdk"]);
            Assert.Equal("enable", p.Properties["Nullable"]);
            Assert.Equal("enable", p.Properties["ImplicitUsings"]);
        });

        Assert.Equal("src/Shop.Domain", real["Shop.Domain"].RootDirectory.Value);
        Assert.Equal("net10.0;net8.0", real["Shop.Domain"].Properties["TargetFrameworks"]);
        Assert.Equal("Shop.Domain", real["Shop.Domain"].Properties["RootNamespace"]);
        Assert.Equal("net10.0", real["Shop.Application"].Properties["TargetFramework"]);
        Assert.Equal("false", real["Shop.Infrastructure"].Properties["UseSqlServer"]);
        Assert.Equal("Exe", real["Shop.Web"].Properties["OutputType"]);
        Assert.Equal("ShopWeb", real["Shop.Web"].Properties["AssemblyName"]);
        Assert.Equal("false", real["Shop.Tests"].Properties["IsPackable"]);
        Assert.Equal("tests/Shop.Tests", real["Shop.Tests"].RootDirectory.Value);
    }

    [Fact]
    public async Task SolutionMemberships_MatchExpectedSection2()
    {
        var model = (await AnalyzeFixtureAsync()).Model;
        var sln = model.Files.Single(f => f.Path.Value == "Shop.sln").Id;
        var slnx = model.Files.Single(f => f.Path.Value == "Shop.slnx").Id;

        foreach (var project in model.Projects.Where(p => !p.IsPlaceholder()))
        {
            var folder = project.Name == "Shop.Tests" ? "/tests/" : "/src/";
            Assert.Equal(
                new[] { new SolutionMembership(sln, folder), new SolutionMembership(slnx, folder) }.ToHashSet(),
                project.SolutionMemberships.ToHashSet());
        }
    }

    [Fact]
    public async Task ProjectReferences_MatchExpectedSection4()
    {
        var model = (await AnalyzeFixtureAsync()).Model;

        var references = model.Relationships
            .Where(r => r.Kind == RelationshipKind.ProjectReference)
            .Select(r => (Name(model, r.Source), Name(model, r.Target), Location(model, Assert.Single(r.Evidence)), r.Level))
            .OrderBy(r => r.Item3, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                ("Shop.Application", "Shop.Domain", "src/Shop.Application/Shop.Application.csproj:4:5", EvidenceLevel.Confirmed),
                ("Shop.Infrastructure", "Shop.Application", "src/Shop.Infrastructure/Shop.Infrastructure.csproj:4:5", EvidenceLevel.Confirmed),
                ("Shop.Infrastructure", "Shop.Domain", "src/Shop.Infrastructure/Shop.Infrastructure.csproj:5:5", EvidenceLevel.Confirmed),
                ("Shop.Infrastructure", "Shop.Legacy", "src/Shop.Infrastructure/Shop.Infrastructure.csproj:6:5", EvidenceLevel.Syntactic),
                ("Shop.Web", "Shop.Application", "src/Shop.Web/Shop.Web.csproj:13:5", EvidenceLevel.Confirmed),
                ("Shop.Web", "Shop.Infrastructure", "src/Shop.Web/Shop.Web.csproj:14:5", EvidenceLevel.Confirmed),
                ("Shop.Tests", "Shop.Domain", "tests/Shop.Tests/Shop.Tests.csproj:13:5", EvidenceLevel.Confirmed),
                ("Shop.Tests", "Shop.Application", "tests/Shop.Tests/Shop.Tests.csproj:14:5", EvidenceLevel.Confirmed),
            ],
            references);

        Assert.All(model.Relationships.Where(r => r.Kind == RelationshipKind.ProjectReference), r =>
        {
            var evidence = Assert.Single(r.Evidence);
            Assert.Equal(DotNetCodes.RuleProjectReference, evidence.Rule);
            Assert.Equal(DotNetCodes.AnalyzerName, evidence.Analyzer.Value);
            Assert.Equal(Run, evidence.AnalysisRunId);
        });
    }

    [Fact]
    public async Task MissingProject_IsPlaceholderWithWarning()
    {
        var analysis = await AnalyzeFixtureAsync();

        var placeholder = Assert.Single(analysis.Model.Projects, p => p.IsPlaceholder());
        Assert.Equal("Shop.Legacy", placeholder.Name);
        Assert.Equal(ProjectKind.Unknown, placeholder.Kind);
        Assert.Equal("src/Shop.Legacy", placeholder.RootDirectory.Value);
        Assert.Null(placeholder.ProjectFileId);
        Assert.Equal("src/Shop.Legacy/Shop.Legacy.csproj", placeholder.Properties[ProjectProperties.ReferencedPath]);
        Assert.DoesNotContain(analysis.Model.Files, f => f.ProjectId == placeholder.Id);

        var warning = Assert.Single(analysis.Diagnostics, d => d.Severity >= DiagnosticSeverity.Warning);
        Assert.Equal(DotNetCodes.MissingProject, warning.Code);
        Assert.Equal(6, warning.Location!.Value.StartLine);
        Assert.Contains("src/Shop.Legacy/Shop.Legacy.csproj", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PackageAndFrameworkReferences_MatchExpectedSections5And6()
    {
        var model = (await AnalyzeFixtureAsync()).Model;

        var packages = model.Relationships
            .Where(r => r.Kind == RelationshipKind.PackageReference)
            .Select(r => (Name(model, r.Source), model.FindExternalDependency(r.Target.Id)!.DisplayName,
                r.Attributes.GetValueOrDefault("versionSpec"), Location(model, Assert.Single(r.Evidence)), r.Level))
            .OrderBy(p => p.Item4, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                ("Shop.Application", "Microsoft.Extensions.DependencyInjection.Abstractions", "10.0.15", "src/Shop.Application/Shop.Application.csproj:8:5", EvidenceLevel.Confirmed),
                ("Shop.Infrastructure", "Microsoft.EntityFrameworkCore.Sqlite", "10.0.15", "src/Shop.Infrastructure/Shop.Infrastructure.csproj:10:5", EvidenceLevel.Confirmed),
                ("Shop.Tests", "xunit.v3", "4.0.3", "tests/Shop.Tests/Shop.Tests.csproj:8:5", EvidenceLevel.Confirmed),
                ("Shop.Tests", "NSubstitute", "5.3.0", "tests/Shop.Tests/Shop.Tests.csproj:9:5", EvidenceLevel.Confirmed),
            ],
            packages);

        var framework = Assert.Single(model.Relationships, r => r.Kind == RelationshipKind.FrameworkReference);
        Assert.Equal("Shop.Web", Name(model, framework.Source));
        Assert.Equal("src/Shop.Web/Shop.Web.csproj:9:5", Location(model, Assert.Single(framework.Evidence)));

        Assert.Equal(
            [
                (PackageEcosystem.NuGet, "microsoft.entityframeworkcore.sqlite"),
                (PackageEcosystem.NuGet, "microsoft.extensions.dependencyinjection.abstractions"),
                (PackageEcosystem.NuGet, "nsubstitute"),
                (PackageEcosystem.NuGet, "xunit.v3"),
                (PackageEcosystem.DotNetFramework, "microsoft.aspnetcore.app"),
            ],
            model.ExternalDependencies.Select(d => (d.Ecosystem, d.Name)).Order());

        Assert.DoesNotContain(model.ExternalDependencies, d => d.Name == "microsoft.entityframeworkcore.sqlserver");
    }

    [Fact]
    public async Task Diagnostics_ContainOnlyTheExpectedWarning()
    {
        var analysis = await AnalyzeFixtureAsync();

        Assert.Equal([DotNetCodes.MissingProject], analysis.Diagnostics.Select(d => d.Code));
        Assert.Equal("1.0.0", analysis.AnalyzerVersions[DotNetCodes.AnalyzerName]);
        Assert.Equal(6, analysis.Statistics.Projects);
        Assert.Equal(13, analysis.Statistics.Relationships);
    }

    [Fact]
    public async Task FilesAreAttachedToTheirProjects()
    {
        var model = (await AnalyzeFixtureAsync()).Model;
        var owner = model.Files.ToDictionary(f => f.Path.Value, f => f.ProjectId is { } id ? model.FindProject(id)!.Name : null);

        Assert.Equal("Shop.Domain", owner["src/Shop.Domain/Orders/Order.cs"]);
        Assert.Equal("Shop.Tests", owner["tests/Shop.Tests/PlaceOrderHandlerTests.cs"]);
        Assert.Equal("Shop.Web", owner["src/Shop.Web/Shop.Web.csproj"]);
        Assert.Null(owner["Shop.sln"]);
        Assert.Null(owner["Directory.Build.props"]);
    }

    [Fact]
    public async Task AnalysisTwice_YieldsIdenticalModels()
    {
        var first = (await AnalyzeFixtureAsync()).Model;
        var second = (await AnalyzeFixtureAsync()).Model;

        Assert.Equal(first.Projects.Select(p => (p.Id, p.Name)), second.Projects.Select(p => (p.Id, p.Name)));
        Assert.Equal(first.Files.Select(f => (f.Id, f.ProjectId, f.Hash.ToString())), second.Files.Select(f => (f.Id, f.ProjectId, f.Hash.ToString())));
        Assert.Equal(first.ExternalDependencies.Select(d => d.Id), second.ExternalDependencies.Select(d => d.Id));
        Assert.Equal(
            first.Relationships.Select(r => (r.Id, r.Level, string.Join(',', r.Evidence.Select(e => e.Id)))),
            second.Relationships.Select(r => (r.Id, r.Level, string.Join(',', r.Evidence.Select(e => e.Id)))));
    }

    [Fact]
    public async Task ReferenceDeclaredInDirectoryBuildProps_PointsToThePropsFile()
    {
        using var repo = new TempDirectory();
        repo.WriteText("Directory.Build.props", "<Project>\n  <ItemGroup>\n    <PackageReference Include=\"Analyzers\" Version=\"1.0.0\" />\n  </ItemGroup>\n</Project>\n");
        repo.WriteText("A/A.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />\n");

        var model = (await AnalyzeAsync(repo.FullPath)).Model;

        var package = Assert.Single(model.Relationships, r => r.Kind == RelationshipKind.PackageReference);
        Assert.Equal("Directory.Build.props:3:5", Location(model, Assert.Single(package.Evidence)));
    }

    [Fact]
    public async Task MissingProjectAtRepositoryRoot_IsReportedWithoutPlaceholder()
    {
        using var repo = new TempDirectory();
        repo.WriteText("A/A.csproj", "<Project>\n  <ItemGroup>\n    <ProjectReference Include=\"..\\Gone.csproj\" />\n  </ItemGroup>\n</Project>\n");

        var analysis = await AnalyzeAsync(repo.FullPath);

        Assert.Single(analysis.Model.Projects);
        Assert.Empty(analysis.Model.Relationships);
        Assert.Equal(DotNetCodes.MissingProject, Assert.Single(analysis.Diagnostics).Code);
    }

    [Fact]
    public async Task SolutionListingMissingProject_IsReported()
    {
        using var repo = new TempDirectory();
        repo.WriteText("All.slnx", "<Solution>\n  <Project Path=\"Gone/Gone.csproj\" />\n</Solution>\n");

        var analysis = await AnalyzeAsync(repo.FullPath);

        Assert.Empty(analysis.Model.Projects);
        var warning = Assert.Single(analysis.Diagnostics);
        Assert.Equal(DotNetCodes.MissingProject, warning.Code);
        Assert.Equal(2, warning.Location!.Value.StartLine);
    }

    [Fact]
    public async Task InvalidProjectFile_IsStillAProjectWithDiagnostic()
    {
        using var repo = new TempDirectory();
        repo.WriteText("A/A.csproj", "<Project><broken");

        var analysis = await AnalyzeAsync(repo.FullPath);

        Assert.Equal("A", Assert.Single(analysis.Model.Projects).Name);
        Assert.Equal(DotNetCodes.InvalidXml, Assert.Single(analysis.Diagnostics).Code);
    }

    [Fact]
    public void AppliesTo_RequiresDotNetMarkers()
    {
        var analyzer = new DotNetProjectSystemAnalyzer();
        var empty = new ScanResult([], new ScanStatistics(), []);

        Assert.False(analyzer.AppliesTo(empty));
    }
}
