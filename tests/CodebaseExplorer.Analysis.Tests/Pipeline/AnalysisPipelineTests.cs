using CodebaseExplorer.Analysis.FileSystem;
using CodebaseExplorer.Analysis.Pipeline;
using CodebaseExplorer.Analysis.Scanning;
using CodebaseExplorer.Application.Analysis;
using CodebaseExplorer.Application.Common;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Evidence;
using CodebaseExplorer.Domain.Runs;
using CodebaseExplorer.TestRepositories;

namespace CodebaseExplorer.Analysis.Tests.Pipeline;

public sealed class AnalysisPipelineTests : IDisposable
{
    private readonly TempDirectory _repo = new();
    private readonly RepositoryId _repositoryId = RepositoryId.New();
    private readonly AnalysisRunId _runId = AnalysisRunId.New();

    public AnalysisPipelineTests()
    {
        _repo.WriteText("src/A/A.csproj", "<Project />");
        _repo.WriteText("src/A/Program.cs", "class P {}");
        _repo.WriteText("src/A/Sub/B/B.csproj", "<Project />");
        _repo.WriteText("src/A/Sub/B/B.cs", "class B {}");
        _repo.WriteText("README.md", "# readme");
    }

    public void Dispose() => _repo.Dispose();

    private AnalysisPipeline CreatePipeline(
        IEnumerable<IProjectSystemAnalyzer>? projectSystems = null,
        IEnumerable<ILanguageAnalyzer>? languages = null)
    {
        var reader = new RepositoryFileReader();
        return new AnalysisPipeline(
            new RepositoryScanner(reader, TimeProvider.System), reader, projectSystems ?? [], languages ?? [], TimeProvider.System);
    }

    private Task<Result<RepositoryAnalysis>> AnalyzeAsync(AnalysisPipeline pipeline, IProgress<AnalysisProgress>? progress = null) =>
        pipeline.AnalyzeAsync(
            new AnalysisRequest(_repositoryId, new RepositoryRoot(_repo.FullPath), _runId),
            progress,
            TestContext.Current.CancellationToken);

    private TestProjectSystemAnalyzer TwoProjects(string id = "test.projects") =>
        new(id, context =>
        {
            var a = Fragments.DotNetProject(context.RepositoryId, "src/A/A.csproj");
            var b = Fragments.DotNetProject(context.RepositoryId, "src/A/Sub/B/B.csproj");
            return new AnalyzerFragment
            {
                Projects = [a, b],
                Relationships = [Fragments.ProjectReference(context.AnalysisRunId, a, b, id, 3, "src/A/A.csproj")],
            };
        });

    [Fact]
    public async Task WithoutAnalyzers_ModelContainsScannedFilesOnly()
    {
        var result = await AnalyzeAsync(CreatePipeline());

        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value.Model.Files.Count);
        Assert.Empty(result.Value.Model.Projects);
        Assert.Equal(5, result.Value.Statistics.FilesScanned);
        Assert.Equal("1.0.0", result.Value.AnalyzerVersions[RepositoryScanner.AnalyzerName]);
    }

    [Fact]
    public async Task ProjectSystemFragment_IsLinkedAndFilesAttachToInnermostProject()
    {
        var result = await AnalyzeAsync(CreatePipeline([TwoProjects()]));

        var model = result.Value.Model;
        var projectA = Assert.Single(model.Projects, p => p.Name == "A");
        var projectB = Assert.Single(model.Projects, p => p.Name == "B");
        var fileOwners = model.Files.ToDictionary(f => f.Path.Value, f => f.ProjectId);

        Assert.Equal(projectA.Id, fileOwners["src/A/Program.cs"]);
        Assert.Equal(projectB.Id, fileOwners["src/A/Sub/B/B.cs"]);
        Assert.Equal(projectB.Id, fileOwners["src/A/Sub/B/B.csproj"]);
        Assert.Null(fileOwners["README.md"]);
        Assert.Single(model.Relationships);
        Assert.Equal("1.0.0-test", result.Value.AnalyzerVersions["test.projects"]);
        Assert.Equal(1, result.Value.Statistics.RelationshipsByLevel[EvidenceLevel.Confirmed]);
    }

    [Fact]
    public async Task FailingAnalyzer_BecomesErrorDiagnostic_AndOtherResultsSurvive()
    {
        var failing = new TestProjectSystemAnalyzer("test.failing", _ => throw new InvalidOperationException("boom"));

        var result = await AnalyzeAsync(CreatePipeline([failing, TwoProjects()]));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Model.Projects.Count);
        var error = Assert.Single(result.Value.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal(AnalysisErrorCodes.AnalyzerFailed, error.Code);
        Assert.Contains("test.failing", error.Message, StringComparison.Ordinal);
        Assert.Contains("boom", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunWithAnalyzerError_IsPartiallySucceededWhenCompleted()
    {
        var failing = new TestProjectSystemAnalyzer("test.failing", _ => throw new InvalidOperationException("boom"));
        var analysis = (await AnalyzeAsync(CreatePipeline([failing]))).Value;

        var run = AnalysisRun.Create(_repositoryId, AnalysisTrigger.Initial, DateTimeOffset.UtcNow);
        run.Start();
        run.Report(analysis.Diagnostics);
        run.Complete(DateTimeOffset.UtcNow, analysis.Statistics);

        Assert.Equal(AnalysisRunStatus.PartiallySucceeded, run.Status);
    }

    [Fact]
    public async Task SameRelationshipFromTwoAnalyzers_IsMergedWithBothEvidenceRecords()
    {
        var result = await AnalyzeAsync(CreatePipeline([TwoProjects("test.first"), TwoProjects("test.second")]));

        var relationship = Assert.Single(result.Value.Model.Relationships);
        Assert.Equal(2, relationship.Occurrences);
        Assert.Equal(["test.first", "test.second"], relationship.Evidence.Select(e => e.Analyzer.Value).Order());
        Assert.Equal(2, result.Value.Model.Projects.Count);
    }

    [Fact]
    public async Task AnalyzerThatDoesNotApply_IsNotRun()
    {
        var analyzer = new TestProjectSystemAnalyzer("test.never", _ => AnalyzerFragment.Empty, appliesTo: _ => false);

        var result = await AnalyzeAsync(CreatePipeline([analyzer]));

        Assert.Empty(analyzer.Calls);
        Assert.False(result.Value.AnalyzerVersions.ContainsKey("test.never"));
    }

    [Fact]
    public async Task InconsistentModel_FailsWithModelInvalid()
    {
        var dangling = new TestProjectSystemAnalyzer("test.dangling", context =>
        {
            var a = Fragments.DotNetProject(context.RepositoryId, "src/A/A.csproj");
            var missing = Fragments.DotNetProject(context.RepositoryId, "src/Missing/Missing.csproj");
            return new AnalyzerFragment
            {
                Projects = [a], // the relationship target is never produced
                Relationships = [Fragments.ProjectReference(context.AnalysisRunId, a, missing, "test.dangling", 3, "src/A/A.csproj")],
            };
        });

        var result = await AnalyzeAsync(CreatePipeline([dangling]));

        Assert.True(result.IsFailure);
        Assert.Equal(AnalysisErrorCodes.ModelInvalid, result.Error.Code);
        Assert.Contains("MissingRelationshipEndpoint", result.Error.Details["firstViolation"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task LanguageAnalyzer_RunsPerProjectWithOnlyItsSupportedFiles()
    {
        var language = new TestLanguageAnalyzer("test.csharp", "csharp", context => new AnalyzerFragment
        {
            Modules = [Fragments.Namespace(context.Project, context.Project.Name)],
        });

        var result = await AnalyzeAsync(CreatePipeline([TwoProjects()], [language]));

        Assert.Equal(2, language.Calls.Count);
        var callA = Assert.Single(language.Calls, c => c.Project.Name == "A");
        Assert.Equal(["src/A/Program.cs"], callA.ProjectFiles.Select(f => f.Path.Value));
        Assert.Equal(2, result.Value.Model.Modules.Count);
        Assert.Equal("2.0.0-test", result.Value.AnalyzerVersions["test.csharp"]);
    }

    [Fact]
    public async Task LanguageAnalyzerFailure_IsIsolatedPerProject()
    {
        var language = new TestLanguageAnalyzer("test.csharp", "csharp", context =>
            context.Project.Name == "B"
                ? throw new InvalidOperationException("parse failed")
                : new AnalyzerFragment { Modules = [Fragments.Namespace(context.Project, "A")] });

        var result = await AnalyzeAsync(CreatePipeline([TwoProjects()], [language]));

        Assert.Single(result.Value.Model.Modules);
        var error = Assert.Single(result.Value.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("project 'B'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Progress_CoversStagesInOrder()
    {
        var progress = new CollectingProgress();

        await AnalyzeAsync(CreatePipeline([TwoProjects()]), progress);

        var stages = progress.Reports.Select(r => r.Stage).Distinct().ToArray();
        Assert.Equal(
            [AnalysisStage.Preparing, AnalysisStage.Scanning, AnalysisStage.DetectingProjects, AnalysisStage.AnalyzingCode,
             AnalysisStage.Linking, AnalysisStage.Validating],
            stages);
    }

    [Fact]
    public async Task Cancellation_IsNotTurnedIntoADiagnostic()
    {
        using var cancellation = new CancellationTokenSource();
        var cancelling = new TestProjectSystemAnalyzer("test.cancel", _ =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreatePipeline([cancelling]).AnalyzeAsync(
            new AnalysisRequest(_repositoryId, new RepositoryRoot(_repo.FullPath), _runId), null, cancellation.Token));
    }

    [Fact]
    public async Task Analysis_IsDeterministic()
    {
        var first = (await AnalyzeAsync(CreatePipeline([TwoProjects("test.b"), TwoProjects("test.a")]))).Value.Model;
        var second = (await AnalyzeAsync(CreatePipeline([TwoProjects("test.a"), TwoProjects("test.b")]))).Value.Model;

        Assert.Equal(first.Files.Select(f => (f.Id, f.ProjectId)), second.Files.Select(f => (f.Id, f.ProjectId)));
        Assert.Equal(first.Projects.Select(p => p.Id), second.Projects.Select(p => p.Id));
        Assert.Equal(
            first.Relationships.SelectMany(r => r.Evidence.Select(e => e.Id)),
            second.Relationships.SelectMany(r => r.Evidence.Select(e => e.Id)));
    }

    private sealed class CollectingProgress : IProgress<AnalysisProgress>
    {
        public List<AnalysisProgress> Reports { get; } = [];

        public void Report(AnalysisProgress value)
        {
            lock (Reports)
            {
                Reports.Add(value);
            }
        }
    }
}
