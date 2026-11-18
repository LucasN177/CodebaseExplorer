using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Dependencies;
using CodebaseExplorer.Domain.Entities;
using CodebaseExplorer.Domain.Evidence;
using CodebaseExplorer.Domain.Files;
using CodebaseExplorer.Domain.Model;
using CodebaseExplorer.Domain.Modules;
using CodebaseExplorer.Domain.Projects;
using CodebaseExplorer.Domain.Relationships;
using CodebaseExplorer.Domain.Repositories;
using CodebaseExplorer.Domain.Runs;

namespace CodebaseExplorer.Storage.Tests;

/// <summary>Hand-built Domain objects covering the optional parts of every core table.</summary>
internal sealed class DomainSamples
{
    private static readonly DateTimeOffset Started = new(2026, 11, 18, 10, 30, 0, TimeSpan.FromHours(1));

    public DomainSamples()
    {
        Repository = new Repository(RepositoryId.New(Started), "shop", RepositoryLocation.Local(Path.Combine(Path.GetTempPath(), "shop")), Started);

        Run = AnalysisRun.Create(Repository.Id, AnalysisTrigger.Initial, Started, "0123456789abcdef0123456789abcdef01234567", "main");
        Run.Start();
        Run.RegisterAnalyzer(AnalyzerId.Create("dotnet.msbuild-static"), "1.0.0");

        Solution = File("Shop.sln", FileClassification.SolutionFile);
        var webManifestPath = RelativePath.Create("src/Web/Web.csproj");
        Web = CodeProject.Create(Repository.Id, "Web", ProjectKind.DotNetProject, Ecosystem.DotNet, LanguageId.CSharp,
            webManifestPath, RelativePath.Create("src/Web"),
            new Dictionary<string, string> { ["TargetFramework"] = "net10.0", ["OutputType"] = "Exe" },
            [new SolutionMembership(Solution.Id, "src"), new SolutionMembership(Solution.Id)]);
        Legacy = CodeProject.Create(Repository.Id, "Legacy", ProjectKind.Unknown, Ecosystem.DotNet, LanguageId.CSharp, null,
            RelativePath.Create("src/Legacy"),
            new Dictionary<string, string>
            {
                [ProjectPropertyKeys.Resolution] = ProjectPropertyKeys.ResolutionMissing,
                [ProjectPropertyKeys.ReferencedPath] = "src/Legacy/Legacy.csproj",
            });

        WebManifest = File(webManifestPath.Value, FileClassification.ProjectManifest, Web.Id);
        Program = File("src/Web/Program.cs", FileClassification.Source, Web.Id, lineCount: 42);
        Readme = File("README.md", FileClassification.Documentation, lineCount: null);

        RootModule = CodeModule.Create(Web, ModuleKind.Namespace, "Web", "Web");
        ChildModule = CodeModule.Create(Web, ModuleKind.Namespace, "Controllers", "Web.Controllers", RootModule);

        // Entities: a partial class with two declarations, a member contained in it, and a target type.
        Controller = CodeEntity.Create(Web, ChildModule, EntityKind.Class, "HomeController", "Web.Controllers.HomeController",
            LanguageId.CSharp, "T:Web.Controllers.HomeController",
            [new SourceLocation(Program.Id, 5, 1, 30, 2), new SourceLocation(Program.Id, 40, 1, 50, 2)],
            Visibility.Public, EntityModifiers.Partial | EntityModifiers.Sealed, metrics: new EntityMetrics(36, 2));
        Index = CodeEntity.Create(Web, ChildModule, EntityKind.Method, "Index", "Web.Controllers.HomeController.Index",
            LanguageId.CSharp, "M:Web.Controllers.HomeController.Index", [SourceLocation.Line(Program.Id, 10, 5, 40)],
            Visibility.Public, EntityModifiers.Async, Controller, "Task<IActionResult> Index()", new EntityMetrics(8, null));
        Service = CodeEntity.Create(Web, RootModule, EntityKind.Interface, "IClock", "Web.IClock", LanguageId.CSharp, "T:Web.IClock",
            [SourceLocation.Line(Program.Id, 60)]);

        Serilog = ExternalDependency.Create(Repository.Id, PackageEcosystem.NuGet, "Serilog.AspNetCore");

        var analyzer = AnalyzerId.Create("dotnet.msbuild-static");
        ProjectReference = Relationship(ElementKind.Project, Web.Id, RelationshipKind.ProjectReference, ElementKind.Project, Legacy.Id,
            id => [AnalysisEvidence.Create(id, analyzer, "1.0.0", EvidenceLevel.Syntactic, "csproj.ProjectReference",
                new SourceLocation(WebManifest.Id, 6, 5, 6, 60), Run.Id, "<ProjectReference Include=\"..\\Legacy\\Legacy.csproj\" />")]);
        PackageReference = Relationship(ElementKind.Project, Web.Id, RelationshipKind.PackageReference, ElementKind.ExternalDependency, Serilog.Id,
            id => [AnalysisEvidence.Create(id, analyzer, "1.0.0", EvidenceLevel.Confirmed, "csproj.PackageReference",
                SourceLocation.Line(WebManifest.Id, 9, 5, 70), Run.Id)],
            new Dictionary<string, string> { ["versionSpec"] = "9.0.0" });
        UsesType = Relationship(ElementKind.Entity, Index.Id, RelationshipKind.UsesType, ElementKind.Entity, Service.Id,
            id =>
            [
                AnalysisEvidence.Create(id, AnalyzerId.Create("csharp.roslyn"), "2.1.0", EvidenceLevel.Confirmed, "csharp.parameter-type",
                    SourceLocation.Line(Program.Id, 11, 9, 20), Run.Id, "IClock clock"),
                AnalysisEvidence.Create(id, AnalyzerId.Create("csharp.roslyn"), "2.1.0", EvidenceLevel.Syntactic, "csharp.identifier",
                    SourceLocation.Line(Program.Id, 14, 13, 18), Run.Id),
                AnalysisEvidence.Create(id, AnalyzerId.Create("heuristic.naming"), "0.1.0", EvidenceLevel.Heuristic, "naming.convention", null, Run.Id),
            ]);

        Run.Report(
        [
            new AnalysisDiagnostic(DiagnosticSeverity.Warning, "dotnet.project-missing", "Referenced project not found.",
                AnalyzerId.Create("dotnet.msbuild-static"), new SourceLocation(WebManifest.Id, 6, 5, 6, 60)),
            new AnalysisDiagnostic(DiagnosticSeverity.Info, "scan.skipped", "Skipped 2 binary files."),
        ]);
        Run.Complete(Started.AddSeconds(3), new AnalysisStatistics(5, 2, 2, 2, 0, 3, 0,
            new Dictionary<EvidenceLevel, int> { [EvidenceLevel.Confirmed] = 2, [EvidenceLevel.Syntactic] = 1 },
            new Dictionary<string, TimeSpan> { ["scan"] = TimeSpan.FromMilliseconds(120.5), ["link"] = TimeSpan.FromSeconds(1) }));
        Repository.MarkAnalyzed(Run);
    }

    public Repository Repository { get; }

    public AnalysisRun Run { get; }

    public RepositoryFile Solution { get; }

    public RepositoryFile WebManifest { get; }

    public RepositoryFile Program { get; }

    public RepositoryFile Readme { get; }

    public CodeProject Web { get; }

    public CodeProject Legacy { get; }

    public CodeModule RootModule { get; }

    public CodeModule ChildModule { get; }

    public IReadOnlyList<RepositoryFile> Files => [Solution, WebManifest, Program, Readme];

    public CodeEntity Controller { get; }

    public CodeEntity Index { get; }

    public CodeEntity Service { get; }

    public IReadOnlyList<CodeEntity> Entities => [Controller, Index, Service];

    public ExternalDependency Serilog { get; }

    public CodeRelationship ProjectReference { get; }

    public CodeRelationship PackageReference { get; }

    public CodeRelationship UsesType { get; }

    public IReadOnlyList<CodeRelationship> Relationships => [ProjectReference, PackageReference, UsesType];

    /// <summary>The whole sample as a validated model (needed for the denormalized container ids).</summary>
    public ArchitectureModel Model
    {
        get
        {
            var model = ArchitectureModel.Create(Repository.Id, Run.Id, Files, [Web, Legacy], [RootModule, ChildModule], Entities, [Serilog], Relationships);
            model.EnsureValid();
            return model;
        }
    }

    private static CodeRelationship Relationship(
        ElementKind sourceKind, ElementId source, RelationshipKind kind, ElementKind targetKind, ElementId target,
        Func<ElementId, IEnumerable<AnalysisEvidence>> evidence, IReadOnlyDictionary<string, string>? attributes = null)
    {
        var sourceRef = new ElementReference(sourceKind, source);
        var targetRef = new ElementReference(targetKind, target);
        return CodeRelationship.Create(sourceRef, kind, targetRef, evidence(CodeRelationship.IdFor(sourceRef, kind, targetRef)), attributes);
    }

    private RepositoryFile File(string path, FileClassification classification, ElementId? projectId = null, int? lineCount = 10) =>
        RepositoryFile.Create(Repository.Id, RelativePath.Create(path), LanguageId.CSharp, classification, 1234,
            ContentHash.Parse("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"), projectId, lineCount);
}
