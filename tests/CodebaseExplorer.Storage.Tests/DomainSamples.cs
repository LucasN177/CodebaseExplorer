using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Evidence;
using CodebaseExplorer.Domain.Files;
using CodebaseExplorer.Domain.Modules;
using CodebaseExplorer.Domain.Projects;
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

    private RepositoryFile File(string path, FileClassification classification, ElementId? projectId = null, int? lineCount = 10) =>
        RepositoryFile.Create(Repository.Id, RelativePath.Create(path), LanguageId.CSharp, classification, 1234,
            ContentHash.Parse("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"), projectId, lineCount);
}
