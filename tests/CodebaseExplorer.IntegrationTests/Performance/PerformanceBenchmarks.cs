using System.Diagnostics;
using CodebaseExplorer.Application.Graph;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Contracts.Graph;
using CodebaseExplorer.TestRepositories;
using Microsoft.Extensions.DependencyInjection;

namespace CodebaseExplorer.IntegrationTests.Performance;

/// <summary>
/// QA-002 benchmarks (explicit, not CI gates): generated repositories are imported through the real composition root
/// (scan, all analyzers, SQLite) and projected. Results go to TEST_STRATEGY.md §8; the MVP targets of
/// PRODUCT_CONCEPT.md §8 are asserted where they exist. Run all with <c>tools/development/Run-Benchmarks.ps1</c> or
/// <c>dotnet test --project tests/CodebaseExplorer.IntegrationTests -c Release --filter-class "*PerformanceBenchmarks" --explicit only --output Detailed</c>.
/// </summary>
public sealed class PerformanceBenchmarks : IAsyncLifetime
{
    private readonly TestApplication _app = new();
    private readonly TempDirectory _repository = new();

    public ValueTask InitializeAsync() => _app.InitializeAsync();

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
        _repository.Dispose();
    }

    [Fact(Explicit = true)]
    public Task DotNet_100Projects_FirstGraphWithin30Seconds() => DotNetAsync(100, TimeSpan.FromSeconds(30));

    [Fact(Explicit = true)]
    public Task DotNet_2000Projects_FirstGraphWithin3Minutes() => DotNetAsync(2_000, TimeSpan.FromMinutes(3));

    [Fact(Explicit = true)]
    public async Task TypeScript_50000Files()
    {
        var files = SyntheticRepository.WriteTypeScript(_repository.FullPath, packages: 250, filesPerPackage: 200);
        await MeasureAsync($"TypeScript monorepo, 250 packages, {files:N0} files", target: null);
    }

    /// <summary>GRAPH-007 acceptance: search on the 2,000-project performance fixture answers within 200 ms.</summary>
    [Fact(Explicit = true)]
    public async Task Search_OnGeneratedRepository_Within200Milliseconds()
    {
        var output = TestContext.Current.TestOutputHelper!;
        var token = TestContext.Current.CancellationToken;
        SyntheticRepository.WriteDotNet(_repository.FullPath, 2_000);
        var summary = (await _app.Services.GetRequiredService<ImportLocalRepository>().ExecuteAsync(_repository.FullPath, null, token)).Value;
        var search = _app.Services.GetRequiredService<CodebaseExplorer.Application.Search.SearchElements>();

        var first = Stopwatch.StartNew();
        var firstResult = (await search.ExecuteAsync(summary.RepositoryId, "Service1", cancellationToken: token)).Value;
        first.Stop(); // includes loading the model (cache miss) and building the name index

        var timings = new List<double>();
        foreach (var term in new[] { "P1234", "service", "Compute3", "model4", "Area2", "remote", "x", "P0001.Area0.Service0" })
        {
            var watch = Stopwatch.StartNew();
            var result = (await search.ExecuteAsync(summary.RepositoryId, term, cancellationToken: token)).Value;
            timings.Add(watch.Elapsed.TotalMilliseconds);
            output.WriteLine($"'{term}': {result.TotalCount:N0} matches in {watch.Elapsed.TotalMilliseconds:F1} ms");
        }

        output.WriteLine($"First search (model load + index): {first.Elapsed.TotalMilliseconds:F0} ms, {firstResult.TotalCount:N0} matches");
        output.WriteLine($"Searches on the loaded model: max {timings.Max():F1} ms, mean {timings.Average():F1} ms");
        Assert.True(timings.Max() < 200, $"Slowest search took {timings.Max():F1} ms; target 200 ms.");
    }

    private async Task DotNetAsync(int projects, TimeSpan target)
    {
        var files = SyntheticRepository.WriteDotNet(_repository.FullPath, projects);
        await MeasureAsync($".NET, {projects:N0} projects, {files:N0} files", target);
    }

    /// <summary>Import (scan, analysis, persistence) and the first project graph, with peak memory of the test process.</summary>
    private async Task MeasureAsync(string scenario, TimeSpan? target)
    {
        var output = TestContext.Current.TestOutputHelper!;
        var token = TestContext.Current.CancellationToken;
        GC.Collect();
        var baseline = Process.GetCurrentProcess().WorkingSet64;

        var total = Stopwatch.StartNew();
        var import = await _app.Services.GetRequiredService<ImportLocalRepository>().ExecuteAsync(_repository.FullPath, null, token);
        var importTime = total.Elapsed;
        Assert.True(import.IsSuccess, import.IsFailure ? import.Error.ToString() : null);
        var summary = import.Value;

        var graph = _app.Services.GetRequiredService<GetGraphView>();
        var query = new GraphQuery { RepositoryId = summary.RepositoryId };
        var projection = Stopwatch.StartNew();
        var view = (await graph.ExecuteAsync(query, token)).Value;
        var firstProjection = projection.Elapsed; // includes loading the model from SQLite (cache miss)
        projection.Restart();
        view = (await graph.ExecuteAsync(query, token)).Value;
        var cachedProjection = projection.Elapsed;
        var firstGraph = importTime + firstProjection;

        using var process = Process.GetCurrentProcess();
        output.WriteLine($"Scenario: {scenario}");
        output.WriteLine($"Status: {summary.Status}; projects {summary.ProjectCount:N0}, relationships {summary.RelationshipCount:N0}, warnings {summary.WarningCount}, errors {summary.ErrorCount}");
        output.WriteLine($"Import (scan + analysis + save): {importTime.TotalSeconds:F1} s");
        output.WriteLine($"First graph after import (load model + project): {firstProjection.TotalMilliseconds:F0} ms; cached projection {cachedProjection.TotalMilliseconds:F0} ms");
        output.WriteLine($"Import to first graph: {firstGraph.TotalSeconds:F1} s");
        output.WriteLine($"Graph: {view.Nodes.Count} nodes, {view.Edges.Count} edges, truncation: {view.Truncation?.Reason ?? "none"}");
        output.WriteLine($"Peak working set of the test process: {process.PeakWorkingSet64 / 1024 / 1024:N0} MB (baseline before import {baseline / 1024 / 1024:N0} MB)");

        Assert.Equal(0, summary.ErrorCount);
        if (target is { } limit)
        {
            Assert.True(firstGraph < limit, $"Import to first graph took {firstGraph}; target {limit}.");
        }
    }
}
