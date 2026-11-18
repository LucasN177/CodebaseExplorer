using CodebaseExplorer.Domain.Projects;
using System.Globalization;
using CodebaseExplorer.Analysis.Scanning;
using CodebaseExplorer.Application.Analysis;
using CodebaseExplorer.Application.Common;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Evidence;
using CodebaseExplorer.Domain.Model;
using CodebaseExplorer.Domain.Relationships;
using CodebaseExplorer.Domain.Runs;

namespace CodebaseExplorer.Analysis.Pipeline;

/// <summary>
/// Orchestrates the analysis stages (ANALYSIS_ENGINE.md §2): prepare → scan → project systems →
/// languages → link → validate. Analyzer failures are isolated per analyzer (stage 2) and per
/// analyzer and project (stage 3) and reported as <c>Error</c> diagnostics.
/// </summary>
internal sealed class AnalysisPipeline(
    RepositoryScanner scanner,
    IRepositoryFileReader fileReader,
    IEnumerable<IProjectSystemAnalyzer> projectSystemAnalyzers,
    IEnumerable<ILanguageAnalyzer> languageAnalyzers,
    TimeProvider timeProvider,
    IEnumerable<IRepositoryLanguageAnalyzer>? repositoryLanguageAnalyzers = null) : IRepositoryAnalyzer
{
    private readonly IRepositoryLanguageAnalyzer[] _repositoryLanguageAnalyzers =
        [.. (repositoryLanguageAnalyzers ?? []).OrderBy(a => a.Descriptor.Id.Value, StringComparer.Ordinal)];

    private const string ScannerVersion = "1.0.0";

    private readonly IProjectSystemAnalyzer[] _projectSystemAnalyzers =
        [.. projectSystemAnalyzers.OrderBy(a => a.Descriptor.Id.Value, StringComparer.Ordinal)];

    private readonly ILanguageAnalyzer[] _languageAnalyzers =
        [.. languageAnalyzers.OrderBy(a => a.Descriptor.Id.Value, StringComparer.Ordinal)];

    public async Task<Result<RepositoryAnalysis>> AnalyzeAsync(
        AnalysisRequest request,
        IProgress<AnalysisProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var durations = new Dictionary<string, TimeSpan>(StringComparer.Ordinal);
        var diagnostics = new List<AnalysisDiagnostic>();
        var versions = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            [RepositoryScanner.AnalyzerName] = ScannerVersion,
        };

        progress?.Report(new AnalysisProgress(AnalysisStage.Preparing, 0));

        // Stage 1 — scan
        var started = timeProvider.GetTimestamp();
        var scan = await scanner.ScanAsync(request.Root, request.Limits, progress, cancellationToken);
        diagnostics.AddRange(scan.Diagnostics);
        durations["scan"] = timeProvider.GetElapsedTime(started);

        // Stage 2 — project systems (parallel, isolated)
        started = timeProvider.GetTimestamp();
        var projectSystemFragments = await RunProjectSystemAnalyzersAsync(request, scan, progress, diagnostics, versions, cancellationToken);
        durations["project-systems"] = timeProvider.GetElapsedTime(started);

        // Stage 3 — languages (per project, isolated)
        started = timeProvider.GetTimestamp();
        var languageFragments = await RunLanguageAnalyzersAsync(
            request, scan, projectSystemFragments, progress, diagnostics, versions, cancellationToken);
        durations["languages"] = timeProvider.GetElapsedTime(started);

        // Stage 4 — link
        progress?.Report(new AnalysisProgress(AnalysisStage.Linking, 0));
        started = timeProvider.GetTimestamp();
        var fragments = projectSystemFragments.Concat(languageFragments).ToArray();
        diagnostics.AddRange(fragments.SelectMany(f => f.Diagnostics));
        var model = ModelLinker.Link(request.RepositoryId, request.AnalysisRunId, scan.Files, fragments);
        durations["link"] = timeProvider.GetElapsedTime(started);

        // Stage 5 — validate
        progress?.Report(new AnalysisProgress(AnalysisStage.Validating, 0));
        started = timeProvider.GetTimestamp();
        var violations = model.Validate();
        durations["validate"] = timeProvider.GetElapsedTime(started);

        if (violations.Count > 0)
        {
            return ApplicationError.Failure(
                AnalysisErrorCodes.ModelInvalid,
                "The analysis produced an inconsistent model; the run cannot be used.",
                new Dictionary<string, string>
                {
                    ["violationCount"] = violations.Count.ToString(CultureInfo.InvariantCulture),
                    ["firstViolation"] = violations[0].ToString(),
                });
        }

        return new RepositoryAnalysis(model, diagnostics, BuildStatistics(model, scan, durations), versions.AsReadOnly());
    }

    private async Task<IReadOnlyList<AnalyzerFragment>> RunProjectSystemAnalyzersAsync(
        AnalysisRequest request,
        ScanResult scan,
        IProgress<AnalysisProgress>? progress,
        List<AnalysisDiagnostic> diagnostics,
        SortedDictionary<string, string> versions,
        CancellationToken cancellationToken)
    {
        var applicable = _projectSystemAnalyzers.Where(a => a.AppliesTo(scan)).ToArray();
        var context = new ProjectSystemContext(
            request.RepositoryId, request.AnalysisRunId, request.Root, scan.Files, request.Limits, fileReader);

        progress?.Report(new AnalysisProgress(AnalysisStage.DetectingProjects, 0, applicable.Length));

        var completed = 0;
        var results = await Task.WhenAll(applicable.Select(async analyzer =>
        {
            var outcome = await RunIsolatedAsync(analyzer.Descriptor, scope: null, () => analyzer.AnalyzeAsync(context, cancellationToken));
            progress?.Report(new AnalysisProgress(
                AnalysisStage.DetectingProjects, Interlocked.Increment(ref completed), applicable.Length, analyzer.Descriptor.Id.Value));
            return outcome;
        }));

        foreach (var analyzer in applicable)
        {
            versions[analyzer.Descriptor.Id.Value] = analyzer.Descriptor.Version;
        }

        diagnostics.AddRange(results.SelectMany(r => r.Failure is null ? [] : new[] { r.Failure }));
        return [.. results.Select(r => r.Fragment)];
    }

    private async Task<IReadOnlyList<AnalyzerFragment>> RunLanguageAnalyzersAsync(
        AnalysisRequest request,
        ScanResult scan,
        IReadOnlyList<AnalyzerFragment> projectSystemFragments,
        IProgress<AnalysisProgress>? progress,
        List<AnalysisDiagnostic> diagnostics,
        SortedDictionary<string, string> versions,
        CancellationToken cancellationToken)
    {
        // Ownership is needed before linking: attach files to their innermost project.
        var projects = projectSystemFragments
            .SelectMany(f => f.Projects)
            .Where(p => !p.IsPlaceholder())
            .DistinctBy(p => p.Id)
            .OrderBy(p => p.NaturalKey.Value, StringComparer.Ordinal)
            .ToArray();
        var owners = projects.OrderByDescending(p => p.RootDirectory.Value.Length).ToArray();
        var filesByProject = scan.Files
            .Select(f => (File: f, Owner: owners.FirstOrDefault(p => f.Path.IsWithin(p.RootDirectory))))
            .Where(x => x.Owner is not null)
            .ToLookup(x => x.Owner!.Id, x => x.File);

        var work = (
            from analyzer in _languageAnalyzers
            from project in projects
            let files = filesByProject[project.Id].Where(f => analyzer.Supports(f.Language)).ToArray()
            where files.Length > 0
            select (analyzer, project, files)).ToArray();

        var repositoryWork = _repositoryLanguageAnalyzers
            .Select(analyzer => (Analyzer: analyzer, Inputs: projects
                .Select(p => new ProjectInput(p, [.. filesByProject[p.Id].Where(f => analyzer.Supports(f.Language))]))
                .Where(input => input.Files.Count > 0)
                .ToArray()))
            .Where(w => w.Inputs.Length > 0)
            .ToArray();

        var total = work.Length + repositoryWork.Sum(w => w.Inputs.Length);
        progress?.Report(new AnalysisProgress(AnalysisStage.AnalyzingCode, 0, total));

        var completed = 0;
        var repositoryResults = new List<(AnalyzerFragment Fragment, AnalysisDiagnostic? Failure)>();
        if (repositoryWork.Length > 0)
        {
            var references = ProjectReferencesOf(projectSystemFragments);
            foreach (var (analyzer, inputs) in repositoryWork)
            {
                var context = new RepositoryLanguageContext(
                    request.RepositoryId, request.AnalysisRunId, request.Root, inputs, references, request.Limits, fileReader,
                    projectName => progress?.Report(new AnalysisProgress(
                        AnalysisStage.AnalyzingCode, Interlocked.Increment(ref completed), total, projectName)));
                repositoryResults.Add(await RunIsolatedAsync(analyzer.Descriptor, scope: null, () => analyzer.AnalyzeAsync(context, cancellationToken)));
                versions[analyzer.Descriptor.Id.Value] = analyzer.Descriptor.Version;
            }
        }

        var results = new (AnalyzerFragment Fragment, AnalysisDiagnostic? Failure)[work.Length];
        var options = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Math.Max(1, request.Limits.MaxDegreeOfParallelism),
        };

        await Parallel.ForAsync(0, work.Length, options, async (index, token) =>
        {
            var (analyzer, project, files) = work[index];
            var context = new LanguageAnalysisContext(
                request.RepositoryId, request.AnalysisRunId, request.Root, project, files, scan.Files, request.Limits, fileReader);

            results[index] = await RunIsolatedAsync(analyzer.Descriptor, project.Name, () => analyzer.AnalyzeProjectAsync(context, token));
            progress?.Report(new AnalysisProgress(
                AnalysisStage.AnalyzingCode, Interlocked.Increment(ref completed), total, project.Name));
        });

        foreach (var analyzer in work.Select(w => w.analyzer).Distinct())
        {
            versions[analyzer.Descriptor.Id.Value] = analyzer.Descriptor.Version;
        }

        var all = repositoryResults.Concat(results).ToArray();
        diagnostics.AddRange(all.SelectMany(r => r.Failure is null ? [] : new[] { r.Failure }));
        return [.. all.Select(r => r.Fragment)];
    }

    /// <summary>Referenced project ids per project, from the stage-2 <c>ProjectReference</c> relationships.</summary>
    private static Dictionary<ElementId, IReadOnlyList<ElementId>> ProjectReferencesOf(IReadOnlyList<AnalyzerFragment> fragments) =>
        fragments
            .SelectMany(f => f.Relationships)
            .Where(r => r.Kind == RelationshipKind.ProjectReference)
            .GroupBy(r => r.Source.Id)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ElementId>)[.. g.Select(r => r.Target.Id).Distinct()]);

    private static async Task<(AnalyzerFragment Fragment, AnalysisDiagnostic? Failure)> RunIsolatedAsync(
        AnalyzerDescriptor descriptor,
        string? scope,
        Func<Task<AnalyzerFragment>> run)
    {
        try
        {
            return (await run() ?? AnalyzerFragment.Empty, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) // Failure isolation (ANALYSIS_ENGINE.md §3 rule 5): any analyzer exception becomes a diagnostic.
        {
            var target = scope is null ? string.Empty : $" for project '{scope}'";
            var failure = new AnalysisDiagnostic(
                DiagnosticSeverity.Error,
                AnalysisErrorCodes.AnalyzerFailed,
                $"Analyzer '{descriptor.Id}' failed{target}: {exception.GetType().Name}: {exception.Message}",
                descriptor.Id);
            return (AnalyzerFragment.Empty, failure);
        }
    }

    private static AnalysisStatistics BuildStatistics(ArchitectureModel model, ScanResult scan, Dictionary<string, TimeSpan> durations)
    {
        var byLevel = model.Relationships
            .GroupBy(r => r.Level)
            .ToDictionary(g => g.Key, g => g.Count());

        return new AnalysisStatistics(
            scan.Statistics.FilesScanned,
            scan.Statistics.FilesSkipped,
            model.Projects.Count,
            model.Modules.Count,
            model.Entities.Count,
            model.Relationships.Count,
            model.ExternalDependencies.Count,
            byLevel.Count == 0 ? null : (IReadOnlyDictionary<EvidenceLevel, int>)byLevel,
            durations);
    }
}
