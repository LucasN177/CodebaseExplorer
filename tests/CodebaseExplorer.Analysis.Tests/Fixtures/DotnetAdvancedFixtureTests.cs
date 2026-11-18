using CodebaseExplorer.Analysis.FileSystem;
using CodebaseExplorer.Analysis.Pipeline;
using CodebaseExplorer.Analysis.ProjectSystems.DotNet;
using CodebaseExplorer.Analysis.Scanning;
using CodebaseExplorer.Application.Analysis;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Dependencies;
using CodebaseExplorer.Domain.Evidence;
using CodebaseExplorer.Domain.Files;
using CodebaseExplorer.Domain.Model;
using CodebaseExplorer.Domain.Projects;
using CodebaseExplorer.Domain.Relationships;
using CodebaseExplorer.Domain.Runs;
using CodebaseExplorer.TestRepositories;

namespace CodebaseExplorer.Analysis.Tests.Fixtures;

/// <summary>
/// REPO-006: fixture <c>dotnet-advanced</c> as regression input for the M1 project-system analysis,
/// checked against sections 1–7 of its EXPECTED.md. Sections 8–10 are asserted by ANALYSIS-005 … 007.
/// </summary>
public sealed class DotnetAdvancedFixtureTests
{
    private const string Fixture = "dotnet-advanced";
    private const string Services = "src/Advanced.Services/Advanced.Services.csproj";

    private static readonly RepositoryId Repository = new(Guid.Parse("0193a1b2-c3d4-7e5f-8071-8293a4b5c6d8"));
    private static readonly AnalysisRunId Run = new(Guid.Parse("0193a1b2-c3d4-7e5f-8071-000000000043"));

    private static string Root => TestRepositoryCatalog.GetFixturePath(Fixture);

    private static async Task<RepositoryAnalysis> AnalyzeAsync()
    {
        var reader = new RepositoryFileReader();
        var pipeline = new AnalysisPipeline(
            new RepositoryScanner(reader, TimeProvider.System), reader, [new DotNetProjectSystemAnalyzer()], [], TimeProvider.System);
        var result = await pipeline.AnalyzeAsync(new AnalysisRequest(Repository, new RepositoryRoot(Root), Run), null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        return result.Value;
    }

    private static string Name(ArchitectureModel model, ElementReference reference) => reference.Kind == ElementKind.Project
        ? model.FindProject(reference.Id)!.Name
        : model.FindExternalDependency(reference.Id)!.DisplayName;

    private static string Location(ArchitectureModel model, SourceLocation location) =>
        $"{model.FindFile(location.FileId)!.Path}:{location.StartLine}:{location.StartColumn}";

    [Fact]
    public async Task Scan_FindsExpectedMarkersAndClassifications()
    {
        var scan = await new RepositoryScanner(new RepositoryFileReader(), TimeProvider.System).ScanAsync(
            new RepositoryRoot(Root), AnalysisLimits.Default, progress: null, TestContext.Current.CancellationToken);

        Assert.Equal(16, scan.Files.Count);
        Assert.Equal(
            new Dictionary<string, (string, FileClassification)>
            {
                ["Advanced.slnx"] = ("DotNetSolutionXml", FileClassification.SolutionFile),
                ["src/Advanced.Core/Advanced.Core.csproj"] = ("DotNetProject", FileClassification.ProjectManifest),
                [Services] = ("DotNetProject", FileClassification.ProjectManifest),
            },
            scan.Files.Where(f => f.Marker is not null).ToDictionary(f => f.Path.Value, f => (f.Marker!.Value.ToString(), f.Classification)));
        Assert.Equal(10, scan.Files.Count(f => f.Classification == FileClassification.Source));
        Assert.Empty(scan.Diagnostics);
    }

    [Fact]
    public void FixtureFiles_UseLfLineEndings_SoDocumentedLocationsStayValid()
    {
        var withCarriageReturn = Directory
            .EnumerateFiles(Root, "*", SearchOption.AllDirectories)
            .Where(path => File.ReadAllBytes(path).Contains((byte)'\r'))
            .Select(path => Path.GetRelativePath(Root, path))
            .ToList();

        Assert.Empty(withCarriageReturn);
    }

    [Fact]
    public async Task Projects_MatchExpectedSection3()
    {
        var model = (await AnalyzeAsync()).Model;
        var projects = model.Projects.ToDictionary(p => p.Name);

        Assert.Equal(["Advanced.Core", "Advanced.Plugins", "Advanced.Services"], projects.Keys.Order());
        Assert.Equal("net10.0;netstandard2.0", projects["Advanced.Core"].Properties["TargetFrameworks"]);
        Assert.Equal("latest", projects["Advanced.Core"].Properties["LangVersion"]);
        Assert.Equal("net10.0", projects["Advanced.Services"].Properties["TargetFramework"]);
        Assert.Equal("true", projects["Advanced.Services"].Properties["EnableLogging"]);
        Assert.Equal("false", projects["Advanced.Services"].Properties["EnableTelemetry"]);

        var placeholder = projects["Advanced.Plugins"];
        Assert.True(placeholder.IsPlaceholder());
        Assert.Equal(ProjectKind.Unknown, placeholder.Kind);
        Assert.Equal("src/Advanced.Plugins", placeholder.RootDirectory.Value);
        Assert.Equal("src/Advanced.Plugins/Advanced.Plugins.csproj", placeholder.Properties[ProjectPropertyKeys.ReferencedPath]);
    }

    [Fact]
    public async Task References_MatchExpectedSections4To6()
    {
        var model = (await AnalyzeAsync()).Model;

        var projectReferences = model.Relationships
            .Where(r => r.Kind == RelationshipKind.ProjectReference)
            .Select(r => (Name(model, r.Source), Name(model, r.Target), Location(model, Assert.Single(r.Evidence).Location!.Value), r.Level))
            .Order()
            .ToArray();
        Assert.Equal(
            [
                ("Advanced.Services", "Advanced.Core", $"{Services}:4:5", EvidenceLevel.Confirmed),
                ("Advanced.Services", "Advanced.Plugins", $"{Services}:5:5", EvidenceLevel.Syntactic),
            ],
            projectReferences);

        var packages = model.Relationships
            .Where(r => r.Kind == RelationshipKind.PackageReference)
            .Select(r => (Name(model, r.Source), Name(model, r.Target), r.Attributes.GetValueOrDefault("versionSpec"),
                Location(model, Assert.Single(r.Evidence).Location!.Value), r.Level))
            .OrderBy(p => p.Item4, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            [
                ("Advanced.Core", "System.Memory", "4.6.3", "src/Advanced.Core/Advanced.Core.csproj:9:5", EvidenceLevel.Confirmed),
                ("Advanced.Services", "Newtonsoft.Json", "13.0.3", $"{Services}:10:5", EvidenceLevel.Confirmed),
                ("Advanced.Services", "Serilog", "4.3.0", $"{Services}:14:5", EvidenceLevel.Confirmed),
                ("Advanced.Services", "Microsoft.Extensions.DependencyInjection.Abstractions", "10.0.15", $"{Services}:9:5", EvidenceLevel.Confirmed),
            ],
            packages);

        Assert.Equal(
            ["microsoft.extensions.dependencyinjection.abstractions", "newtonsoft.json", "serilog", "system.memory"],
            model.ExternalDependencies.Where(d => d.Ecosystem == PackageEcosystem.NuGet).Select(d => d.Name).Order());
        Assert.Equal(6, model.Relationships.Count);
        Assert.DoesNotContain(model.ExternalDependencies, d => d.Name == "opentelemetry");
    }

    [Fact]
    public async Task Diagnostics_MatchExpectedSection7()
    {
        var analysis = await AnalyzeAsync();

        var diagnostics = analysis.Diagnostics
            .Select(d => (d.Severity, d.Code, Location(analysis.Model, d.Location!.Value)))
            .OrderBy(d => d.Item3, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            [
                (DiagnosticSeverity.Warning, DotNetCodes.UnsupportedCondition, $"{Services}:21:3"),
                (DiagnosticSeverity.Warning, DotNetCodes.MissingProject, $"{Services}:5:5"),
            ],
            diagnostics);
    }
}
