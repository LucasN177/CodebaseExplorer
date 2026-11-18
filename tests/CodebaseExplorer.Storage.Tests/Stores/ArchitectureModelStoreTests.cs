using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Files;
using CodebaseExplorer.Domain.Model;
using CodebaseExplorer.Domain.Repositories;
using CodebaseExplorer.Domain.Runs;
using CodebaseExplorer.Storage.Stores;
using Microsoft.EntityFrameworkCore;

namespace CodebaseExplorer.Storage.Tests.Stores;

/// <summary>STORE-004: saving runs, replacing models transactionally and reading the current model.</summary>
public sealed class ArchitectureModelStoreTests : IDisposable
{
    private readonly TestDatabase _database = new();
    private readonly DomainSamples _samples = new();
    private readonly ArchitectureModelStore _store;

    public ArchitectureModelStoreTests() => _store = new ArchitectureModelStore(new TestContextFactory(_database));

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _store.Dispose();
        _database.Dispose();
    }

    [Fact]
    public async Task SavedModel_IsLoadedAgain()
    {
        await _store.SaveRunAsync(_samples.Repository, _samples.Run, _samples.Model, Token);

        var stored = await _store.LoadCurrentModelAsync(_samples.Repository.Id, Token);

        Assert.NotNull(stored);
        Assert.Equal(_samples.Run.Id, stored.Run.Id);
        Assert.Equal(_samples.Run.Id, stored.Repository.LastAnalysisRunId);
        Assert.Equal(_samples.Repository.Location, stored.Repository.Location);
        AssertSameModel(_samples.Model, stored.Model);
    }

    [Fact]
    public async Task SavedModel_SurvivesANewStoreInstance()
    {
        await _store.SaveRunAsync(_samples.Repository, _samples.Run, _samples.Model, Token);

        using var restarted = new ArchitectureModelStore(new TestContextFactory(_database));
        var stored = await restarted.LoadCurrentModelAsync(_samples.Repository.Id, Token);

        AssertSameModel(_samples.Model, stored!.Model);
    }

    [Fact]
    public async Task ProjectAtTheRepositoryRoot_IsLoadedAgain()
    {
        // Found with the Node analyzer (ANALYSIS-009): the root directory is stored as "" and must map back to the root.
        var repository = Unanalyzed(_samples.Repository);
        var run = NewRun(AnalysisTrigger.Manual);
        run.Complete(run.StartedAt.AddSeconds(1), AnalysisStatistics.Empty);
        repository.MarkAnalyzed(run);
        var manifest = RelativePath.Create("package.json");
        var project = Domain.Projects.CodeProject.Create(repository.Id, "root", Domain.Projects.ProjectKind.NodePackage, Domain.Projects.Ecosystem.Node,
            LanguageId.JavaScript, manifest, RelativePath.Root);
        var file = RepositoryFile.Create(repository.Id, manifest, LanguageId.Json, FileClassification.ProjectManifest, 2,
            ContentHash.Parse("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"), project.Id, 1);
        var model = ArchitectureModel.Create(repository.Id, run.Id, [file], [project], [], [], [], []);

        await _store.SaveRunAsync(repository, run, model, Token);

        using var restarted = new ArchitectureModelStore(new TestContextFactory(_database));
        var stored = await restarted.LoadCurrentModelAsync(repository.Id, Token);
        Assert.True(Assert.Single(stored!.Model.Projects).RootDirectory.IsRoot);
    }

    [Fact]
    public async Task UnknownOrUnanalyzedRepository_HasNoModel()
    {
        Assert.Null(await _store.LoadCurrentModelAsync(RepositoryId.New(), Token));

        var failed = NewRun(AnalysisTrigger.Initial);
        failed.Fail(failed.StartedAt.AddSeconds(1), [new AnalysisDiagnostic(DiagnosticSeverity.Error, "analysis.failed", "boom")]);
        var repository = Unanalyzed(_samples.Repository);
        await _store.SaveRunAsync(repository, failed, null, Token);

        Assert.Null(await _store.LoadCurrentModelAsync(repository.Id, Token));
        Assert.NotNull(await _store.FindRepositoryAsync(repository.Id, Token)); // registered, run recorded
        Assert.Equal(1L, await _database.ScalarAsync("SELECT count(*) FROM AnalysisRuns"));
    }

    [Theory]
    [InlineData(AnalysisRunStatus.Failed)]
    [InlineData(AnalysisRunStatus.Cancelled)]
    public async Task FailedOrCancelledRun_IsRecorded_ButNeverChangesTheModel(AnalysisRunStatus status)
    {
        await _store.SaveRunAsync(_samples.Repository, _samples.Run, _samples.Model, Token);
        var counts = await RowCountsAsync();
        var run = NewRun(AnalysisTrigger.Manual);
        if (status == AnalysisRunStatus.Failed)
        {
            run.Fail(run.StartedAt.AddSeconds(1), [new AnalysisDiagnostic(DiagnosticSeverity.Error, "analysis.failed", "boom")]);
        }
        else
        {
            run.Cancel(run.StartedAt.AddSeconds(1));
        }

        await _store.SaveRunAsync(_samples.Repository, run, model: null, Token);

        Assert.Equal(counts, await RowCountsAsync());
        Assert.Equal(_samples.Run.Id, (await _store.LoadCurrentModelAsync(_samples.Repository.Id, Token))!.Run.Id);
        Assert.Equal(2L, await _database.ScalarAsync("SELECT count(*) FROM AnalysisRuns"));
        Assert.Equal(status.ToString(), await _database.ScalarAsync($"SELECT Status FROM AnalysisRuns WHERE Trigger = 'Manual'"));
    }

    [Fact]
    public async Task NewSuccessfulRun_ReplacesTheWholeModel()
    {
        await _store.SaveRunAsync(_samples.Repository, _samples.Run, _samples.Model, Token);
        var repository = (await _store.FindRepositoryAsync(_samples.Repository.Id, Token))!;
        var run = NewRun(AnalysisTrigger.Manual);
        run.Complete(run.StartedAt.AddSeconds(1), AnalysisStatistics.Empty);
        repository.MarkAnalyzed(run);
        var readme = RepositoryFile.Create(repository.Id, RelativePath.Create("README.md"), LanguageId.Markdown, FileClassification.Documentation, 5,
            ContentHash.Parse("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"));
        var smaller = ArchitectureModel.Create(repository.Id, run.Id, [readme], [], [], [], [], []);

        await _store.SaveRunAsync(repository, run, smaller, Token);

        var stored = await _store.LoadCurrentModelAsync(repository.Id, Token);
        Assert.Equal(run.Id, stored!.Run.Id);
        Assert.Equal([readme.Id], stored.Model.Files.Select(f => f.Id));
        Assert.Equal(new Dictionary<string, long>
        {
            ["Files"] = 1, ["Projects"] = 0, ["Modules"] = 0, ["CodeEntities"] = 0, ["CodeEntityDeclarations"] = 0,
            ["ExternalDependencies"] = 0, ["CodeRelationships"] = 0, ["AnalysisEvidence"] = 0,
        }, await RowCountsAsync());
    }

    [Fact]
    public async Task FailureWhileReplacing_RollsBack_AndKeepsThePreviousModel()
    {
        await _store.SaveRunAsync(_samples.Repository, _samples.Run, _samples.Model, Token);
        var counts = await RowCountsAsync();
        using var failing = new ArchitectureModelStore(new TestContextFactory(_database, failOnCommandContaining: "INSERT INTO \"CodeRelationships\""));
        var repository = (await failing.FindRepositoryAsync(_samples.Repository.Id, Token))!;
        var run = NewRun(AnalysisTrigger.Manual);
        run.Complete(run.StartedAt.AddSeconds(1), AnalysisStatistics.Empty);
        repository.MarkAnalyzed(run);
        var previous = _samples.Model; // same content under a new run
        var model = ArchitectureModel.Create(repository.Id, run.Id, previous.Files, previous.Projects, previous.Modules,
            previous.Entities, previous.ExternalDependencies, previous.Relationships);

        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => failing.SaveRunAsync(repository, run, model, Token));
        Assert.Equal("Simulated failure while saving.", failure.InnerException?.Message);

        Assert.Equal(counts, await RowCountsAsync());
        Assert.Equal(1L, await _database.ScalarAsync("SELECT count(*) FROM AnalysisRuns")); // the new run was rolled back too
        Assert.Equal(_samples.Run.Id, (await _store.LoadCurrentModelAsync(_samples.Repository.Id, Token))!.Run.Id);
    }

    [Fact]
    public async Task LoadedModel_IsCachedPerRun_AndInvalidatedBySaving()
    {
        await _store.SaveRunAsync(_samples.Repository, _samples.Run, _samples.Model, Token);

        var first = await _store.LoadCurrentModelAsync(_samples.Repository.Id, Token);
        var second = await _store.LoadCurrentModelAsync(_samples.Repository.Id, Token);
        Assert.Same(first, second);

        var cancelled = NewRun(AnalysisTrigger.Manual);
        cancelled.Cancel(cancelled.StartedAt.AddSeconds(1));
        await _store.SaveRunAsync(_samples.Repository, cancelled, null, Token);

        var third = await _store.LoadCurrentModelAsync(_samples.Repository.Id, Token);
        Assert.NotSame(first, third);
        Assert.Equal(first!.Run.Id, third!.Run.Id);
    }

    [Fact]
    public async Task FindRepositoryByRoot_FindsTheRegisteredFolder()
    {
        await _store.SaveRunAsync(_samples.Repository, _samples.Run, _samples.Model, Token);
        var path = _samples.Repository.Location.LocalPath;

        Assert.Equal(_samples.Repository.Id, (await _store.FindRepositoryByRootAsync(path, Token))?.Id);
        Assert.Null(await _store.FindRepositoryByRootAsync(path + "-other", Token));
        var differentCase = await _store.FindRepositoryByRootAsync(path.ToUpperInvariant(), Token);
        Assert.Equal(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS(), differentCase is not null);
    }

    [Fact]
    public async Task SavingAModel_RequiresAMatchingSucceededRun()
    {
        var running = NewRun(AnalysisTrigger.Manual);

        await Assert.ThrowsAsync<ArgumentException>(() => _store.SaveRunAsync(_samples.Repository, running, null, Token)); // not finished
        await Assert.ThrowsAsync<ArgumentException>(() => _store.SaveRunAsync(_samples.Repository, _samples.Run, null, Token)); // model missing
        await Assert.ThrowsAsync<ArgumentException>(() => _store.SaveRunAsync(Unanalyzed(_samples.Repository), _samples.Run, _samples.Model, Token)); // not marked
    }

    private AnalysisRun NewRun(AnalysisTrigger trigger)
    {
        var run = AnalysisRun.Create(_samples.Repository.Id, trigger, _samples.Run.StartedAt.AddMinutes(5));
        run.Start();
        return run;
    }

    private static Repository Unanalyzed(Repository repository) => SampleStore.Unanalyzed(repository);

    private async Task<Dictionary<string, long>> RowCountsAsync()
    {
        var counts = new Dictionary<string, long>();
        foreach (var table in new[] { "Files", "Projects", "Modules", "CodeEntities", "CodeEntityDeclarations", "ExternalDependencies", "CodeRelationships", "AnalysisEvidence" })
        {
            counts[table] = (long)(await _database.ScalarAsync($"SELECT count(*) FROM {table}"))!;
        }

        return counts;
    }

    private static void AssertSameModel(ArchitectureModel expected, ArchitectureModel actual)
    {
        Assert.Equal(expected.RepositoryId, actual.RepositoryId);
        Assert.Equal(expected.AnalysisRunId, actual.AnalysisRunId);
        Assert.Equal(expected.Files.Select(f => f.Id.Value).Order(), actual.Files.Select(f => f.Id.Value).Order());
        Assert.Equal(expected.Projects.Select(p => p.Id.Value).Order(), actual.Projects.Select(p => p.Id.Value).Order());
        Assert.Equal(expected.Modules.Select(m => m.Id.Value).Order(), actual.Modules.Select(m => m.Id.Value).Order());
        Assert.Equal(expected.Entities.Select(e => e.Id.Value).Order(), actual.Entities.Select(e => e.Id.Value).Order());
        Assert.Equal(expected.ExternalDependencies.Select(d => d.Id.Value).Order(), actual.ExternalDependencies.Select(d => d.Id.Value).Order());
        Assert.Equal(expected.Relationships.Select(r => (r.Id.Value, r.Occurrences)).OrderBy(r => r.Value), actual.Relationships.Select(r => (r.Id.Value, r.Occurrences)).OrderBy(r => r.Value));
    }
}
