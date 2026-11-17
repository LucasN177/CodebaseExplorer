using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Runs;
using CodebaseExplorer.Domain.Tests.Projects;

namespace CodebaseExplorer.Domain.Tests.Runs;

public sealed class AnalysisRunTests
{
    private static readonly DateTimeOffset Started = new(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);
    private static readonly AnalysisDiagnostic Error = new(DiagnosticSeverity.Error, "CE-TEST-1", "Analyzer crashed.");
    private static readonly AnalysisDiagnostic Warning = new(DiagnosticSeverity.Warning, "CE-TEST-2", "Missing project file.");

    private static AnalysisRun NewRun() => AnalysisRun.Create(Sample.Repository, AnalysisTrigger.Initial, Started);

    [Fact]
    public void Create_IsPendingWithTimeOrderedId()
    {
        var run = NewRun();

        Assert.Equal(AnalysisRunStatus.Pending, run.Status);
        Assert.Equal(7, run.Id.Value.Version);
        Assert.Null(run.CompletedAt);
        Assert.False(run.IsTerminal);
    }

    [Fact]
    public void Complete_WithoutErrors_Succeeds()
    {
        var run = NewRun();
        run.Start();
        run.Report([Warning]);

        run.Complete(Started.AddSeconds(5), new AnalysisStatistics(10, 1, 2, 0, 0, 1, 0));

        Assert.Equal(AnalysisRunStatus.Succeeded, run.Status);
        Assert.Equal(Started.AddSeconds(5), run.CompletedAt);
        Assert.Equal(10, run.Statistics.FilesScanned);
        Assert.True(run.ProducedModel);
    }

    [Fact]
    public void Complete_WithErrorDiagnostics_IsPartiallySucceeded()
    {
        var run = NewRun();
        run.Start();
        run.Report([Error]);

        run.Complete(Started, AnalysisStatistics.Empty);

        Assert.Equal(AnalysisRunStatus.PartiallySucceeded, run.Status);
        Assert.True(run.ProducedModel);
    }

    [Fact]
    public void Complete_WhenPending_Throws() =>
        Assert.Throws<InvalidOperationException>(() => NewRun().Complete(Started, AnalysisStatistics.Empty));

    [Fact]
    public void Start_Twice_Throws()
    {
        var run = NewRun();
        run.Start();

        Assert.Throws<InvalidOperationException>(run.Start);
    }

    [Fact]
    public void Fail_RequiresErrorDiagnostic()
    {
        var run = NewRun();
        run.Start();

        Assert.Throws<ArgumentException>(() => run.Fail(Started, [Warning]));

        run.Fail(Started.AddSeconds(1), [Error]);
        Assert.Equal(AnalysisRunStatus.Failed, run.Status);
        Assert.False(run.ProducedModel);
    }

    [Fact]
    public void Cancel_FromPendingOrRunning_IsAllowed()
    {
        var pending = NewRun();
        pending.Cancel(Started);

        var running = NewRun();
        running.Start();
        running.Cancel(Started);

        Assert.Equal(AnalysisRunStatus.Cancelled, pending.Status);
        Assert.Equal(AnalysisRunStatus.Cancelled, running.Status);
    }

    [Fact]
    public void TerminalRun_RejectsFurtherChanges()
    {
        var run = NewRun();
        run.Cancel(Started);

        Assert.Throws<InvalidOperationException>(run.Start);
        Assert.Throws<InvalidOperationException>(() => run.Cancel(Started));
        Assert.Throws<InvalidOperationException>(() => run.Fail(Started, [Error]));
        Assert.Throws<InvalidOperationException>(() => run.Report([Warning]));
        Assert.Throws<InvalidOperationException>(() => run.RegisterAnalyzer(AnalyzerId.Create("x"), "1"));
    }

    [Fact]
    public void Finish_BeforeStart_Throws()
    {
        var run = NewRun();

        Assert.Throws<ArgumentException>(() => run.Cancel(Started.AddTicks(-1)));
    }

    [Fact]
    public void RegisterAnalyzer_RecordsVersions()
    {
        var run = NewRun();
        run.RegisterAnalyzer(AnalyzerId.Create("dotnet.msbuild-static"), " 1.2.0 ");

        Assert.Equal("1.2.0", run.AnalyzerVersions["dotnet.msbuild-static"]);
    }

    [Theory]
    [InlineData("8f3e2b1c9d0a7f6e5d4c3b2a1908f7e6d5c4b3a2")]
    [InlineData("8F3E2B1C9D0A7F6E5D4C3B2A1908F7E6D5C4B3A2")]
    public void Create_AcceptsFullCommitSha(string sha) =>
        Assert.Equal(sha.ToLowerInvariant(), AnalysisRun.Create(Sample.Repository, AnalysisTrigger.Manual, Started, sha, "main").CommitSha);

    [Theory]
    [InlineData("8f3e2b1")]
    [InlineData("not-a-sha-not-a-sha-not-a-sha-not-a-sha!")]
    public void Create_RejectsAbbreviatedOrInvalidSha(string sha) =>
        Assert.Throws<ArgumentException>(() => AnalysisRun.Create(Sample.Repository, AnalysisTrigger.Manual, Started, sha));

    [Fact]
    public void Restore_RoundTripsTerminalRun()
    {
        var id = AnalysisRunId.New(Started);

        var run = AnalysisRun.Restore(id, Sample.Repository, AnalysisTrigger.Manual, AnalysisRunStatus.PartiallySucceeded,
            Started, Started.AddMinutes(1), null, "main", new Dictionary<string, string> { ["csharp.roslyn"] = "5.12.0" },
            AnalysisStatistics.Empty, [Error]);

        Assert.Equal(id, run.Id);
        Assert.Equal(AnalysisRunStatus.PartiallySucceeded, run.Status);
        Assert.Single(run.Diagnostics);
        Assert.Equal("5.12.0", run.AnalyzerVersions["csharp.roslyn"]);
    }

    [Theory]
    [InlineData(AnalysisRunStatus.Succeeded, true, true)]            // succeeded despite an error diagnostic
    [InlineData(AnalysisRunStatus.PartiallySucceeded, false, true)]  // partial without an error diagnostic
    [InlineData(AnalysisRunStatus.Failed, false, true)]              // failed without an error diagnostic
    [InlineData(AnalysisRunStatus.Running, false, true)]             // non-terminal with a completion time
    [InlineData(AnalysisRunStatus.Cancelled, false, false)]          // terminal without a completion time
    public void Restore_RejectsInconsistentState(AnalysisRunStatus status, bool withError, bool hasCompletionTime) =>
        Assert.Throws<ArgumentException>(() => AnalysisRun.Restore(AnalysisRunId.New(), Sample.Repository, AnalysisTrigger.Initial,
            status, Started, hasCompletionTime ? Started : null, null, null, new Dictionary<string, string>(),
            AnalysisStatistics.Empty, withError ? [Error] : []));
}
