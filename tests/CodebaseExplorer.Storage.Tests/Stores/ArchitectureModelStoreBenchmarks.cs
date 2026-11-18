using System.Diagnostics;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Entities;
using CodebaseExplorer.Domain.Evidence;
using CodebaseExplorer.Domain.Files;
using CodebaseExplorer.Domain.Model;
using CodebaseExplorer.Domain.Modules;
using CodebaseExplorer.Domain.Projects;
using CodebaseExplorer.Domain.Relationships;
using CodebaseExplorer.Domain.Repositories;
using CodebaseExplorer.Domain.Runs;
using CodebaseExplorer.Storage.Stores;

namespace CodebaseExplorer.Storage.Tests.Stores;

/// <summary>
/// STORE-004 acceptance benchmark (explicit, not a CI gate): saving 1,000,000 synthetic relationships must
/// stay below 1.5 GB process memory. Run with:
/// <c>dotnet test --project tests/CodebaseExplorer.Storage.Tests --filter-method "*SaveOneMillionRelationships*" --explicit only --output Detailed</c>.
/// Results are recorded in TEST_STRATEGY.md §8.
/// </summary>
public sealed class ArchitectureModelStoreBenchmarks : IDisposable
{
    private const int EntityCount = 1_001; // 1,000 sources × 1,000 targets each (self excluded) = 1,000,000 relationships
    private const long MemoryLimitBytes = 1_536L * 1024 * 1024;

    private readonly TestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact(Explicit = true)]
    public async Task SaveOneMillionRelationships_StaysBelowOnePointFiveGigabytes()
    {
        var output = TestContext.Current.TestOutputHelper;
        var started = DateTimeOffset.UtcNow;
        var repository = Repository.Register("synthetic", RepositoryLocation.Local(Path.Combine(Path.GetTempPath(), "synthetic")), started);
        var run = AnalysisRun.Create(repository.Id, AnalysisTrigger.Initial, started);
        run.Start();

        var build = Stopwatch.StartNew();
        var model = BuildModel(repository.Id, run.Id);
        build.Stop();
        run.Complete(DateTimeOffset.UtcNow, AnalysisStatistics.Empty);
        repository.MarkAnalyzed(run);
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        output?.WriteLine($"Built {model.Relationships.Count:N0} relationships in {build.Elapsed.TotalSeconds:F1} s; managed heap after GC {Mb(GC.GetTotalMemory(false))} MB; working set {Mb(Environment.WorkingSet)} MB; peak so far {Mb(Process.GetCurrentProcess().PeakWorkingSet64)} MB.");

        using var store = new ArchitectureModelStore(new TestContextFactory(_database));
        long maxHeap = 0, maxWorkingSet = 0;
        using var sampling = new CancellationTokenSource();
        var sampler = Task.Run(async () =>
        {
            while (!sampling.IsCancellationRequested)
            {
                maxHeap = Math.Max(maxHeap, GC.GetTotalMemory(false));
                maxWorkingSet = Math.Max(maxWorkingSet, Environment.WorkingSet);
                await Task.Delay(100, CancellationToken.None);
            }
        }, CancellationToken.None);
        var save = Stopwatch.StartNew();
        await store.SaveRunAsync(repository, run, model, TestContext.Current.CancellationToken);
        save.Stop();
        await sampling.CancelAsync();
        await sampler;
        output?.WriteLine($"During save: max managed heap {Mb(maxHeap)} MB, max working set {Mb(maxWorkingSet)} MB.");

        using var process = Process.GetCurrentProcess();
        process.Refresh();
        output?.WriteLine($"Saved in {save.Elapsed.TotalSeconds:F1} s; peak working set {Mb(process.PeakWorkingSet64)} MB; " +
            $"database {Mb(new FileInfo(_database.FilePath).Length + WalLength())} MB.");

        Assert.Equal(1_000_000L, await _database.ScalarAsync("SELECT count(*) FROM CodeRelationships"));
        Assert.Equal(1_000_000L, await _database.ScalarAsync("SELECT count(*) FROM AnalysisEvidence"));
        Assert.True(process.PeakWorkingSet64 < MemoryLimitBytes, $"Peak working set {Mb(process.PeakWorkingSet64)} MB exceeds 1536 MB.");
    }

    private long WalLength() => File.Exists(_database.FilePath + "-wal") ? new FileInfo(_database.FilePath + "-wal").Length : 0;

    private static long Mb(long bytes) => bytes / (1024 * 1024);

    private static ArchitectureModel BuildModel(RepositoryId repositoryId, AnalysisRunId runId)
    {
        var project = CodeProject.Create(repositoryId, "Big", ProjectKind.DotNetProject, Ecosystem.DotNet, LanguageId.CSharp,
            null, RelativePath.Create("src/Big"));
        var file = RepositoryFile.Create(repositoryId, RelativePath.Create("src/Big/Types.cs"), LanguageId.CSharp, FileClassification.Source,
            1, ContentHash.Parse("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"), project.Id);
        var module = CodeModule.Create(project, ModuleKind.Namespace, "Big", "Big");
        var entities = Enumerable.Range(0, EntityCount)
            .Select(i => CodeEntity.Create(project, module, EntityKind.Class, $"T{i}", $"Big.T{i}", LanguageId.CSharp, $"T:Big.T{i}",
                [SourceLocation.Line(file.Id, i + 1)]))
            .ToArray();

        var analyzer = AnalyzerId.Create("benchmark.synthetic");
        var relationships = new List<CodeRelationship>(1_000_000);
        for (var source = 0; source < EntityCount - 1; source++)
        {
            for (var target = 0; target < EntityCount; target++)
            {
                if (source == target)
                {
                    continue;
                }

                var sourceRef = new ElementReference(ElementKind.Entity, entities[source].Id);
                var targetRef = new ElementReference(ElementKind.Entity, entities[target].Id);
                var id = CodeRelationship.IdFor(sourceRef, RelationshipKind.UsesType, targetRef);
                relationships.Add(CodeRelationship.Create(sourceRef, RelationshipKind.UsesType, targetRef,
                    [AnalysisEvidence.Create(id, analyzer, "1", EvidenceLevel.Confirmed, "synthetic.uses", SourceLocation.Line(file.Id, source + 1), runId)]));
            }
        }

        return ArchitectureModel.Create(repositoryId, runId, [file], [project], [module], entities, [], relationships);
    }
}
