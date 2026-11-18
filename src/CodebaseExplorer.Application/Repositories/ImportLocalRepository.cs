using System.Collections.Concurrent;
using CodebaseExplorer.Application.Analysis;
using CodebaseExplorer.Application.Common;
using CodebaseExplorer.Contracts.Repositories;
using CodebaseExplorer.Domain.Model;
using CodebaseExplorer.Domain.Repositories;
using CodebaseExplorer.Domain.Runs;

namespace CodebaseExplorer.Application.Repositories;

/// <summary>
/// Use case: import (or re-import) a local folder (USER_WORKFLOWS.md WF-01). Validates the folder,
/// runs the analysis as one <see cref="AnalysisRun"/>, and saves the run and the resulting model in the
/// <see cref="IArchitectureModelStore"/>. Only one import per folder runs at a time.
/// </summary>
/// <remarks>
/// Failed and cancelled runs are recorded, but the stored model is left unchanged (a previous model of
/// the folder stays available); on cancellation <see cref="OperationCanceledException"/> propagates.
/// </remarks>
public sealed class ImportLocalRepository(
    IRepositoryRootValidator rootValidator,
    IRepositoryAnalyzer analyzer,
    IArchitectureModelStore store,
    TimeProvider timeProvider)
{
    private readonly ConcurrentDictionary<string, byte> _running = new(RootPathComparer.Instance);

    public async Task<Result<RepositoryImportSummaryDto>> ExecuteAsync(
        string path,
        IProgress<AnalysisProgress>? progress,
        CancellationToken cancellationToken)
    {
        var validated = rootValidator.Validate(path);
        if (!validated.TryGetValue(out var root, out var validationError))
        {
            return validationError;
        }

        if (!_running.TryAdd(root.FullPath, 0))
        {
            return ApplicationError.Conflict(RepositoryErrorCodes.ImportInProgress,
                "This folder is already being analyzed.", new Dictionary<string, string> { ["path"] = root.FullPath });
        }

        try
        {
            return await ImportAsync(root, progress, cancellationToken);
        }
        finally
        {
            _running.TryRemove(root.FullPath, out _);
        }
    }

    private async Task<Result<RepositoryImportSummaryDto>> ImportAsync(
        RepositoryRoot root, IProgress<AnalysisProgress>? progress, CancellationToken cancellationToken)
    {
        var started = timeProvider.GetUtcNow();
        var previous = await store.FindRepositoryByRootAsync(root.FullPath, cancellationToken);
        var repository = previous
            ?? Repository.Register(NameOf(root), RepositoryLocation.Local(root.FullPath), started);

        var run = AnalysisRun.Create(repository.Id, previous is null ? AnalysisTrigger.Initial : AnalysisTrigger.Manual, started);
        run.Start();

        Result<RepositoryAnalysis> result;
        try
        {
            result = await analyzer.AnalyzeAsync(new AnalysisRequest(repository.Id, root, run.Id), progress, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            run.Cancel(timeProvider.GetUtcNow());
            // Recorded for the run history; the stored model stays as it was. Not cancellable: the user's
            // cancellation already happened and this write is short.
            await store.SaveRunAsync(repository, run, model: null, CancellationToken.None);
            throw;
        }

        if (!result.TryGetValue(out var analysis, out var error))
        {
            run.Fail(timeProvider.GetUtcNow(), [new AnalysisDiagnostic(DiagnosticSeverity.Error, error.Code, error.Message)]);
            await store.SaveRunAsync(repository, run, model: null, cancellationToken);
            return error;
        }

        foreach (var (analyzerId, version) in analysis.AnalyzerVersions)
        {
            run.RegisterAnalyzer(Domain.Common.AnalyzerId.Create(analyzerId), version);
        }

        run.Report(analysis.Diagnostics);
        run.Complete(timeProvider.GetUtcNow(), analysis.Statistics);
        repository.MarkAnalyzed(run);

        progress?.Report(new AnalysisProgress(AnalysisStage.Saving, 0, 1));
        await store.SaveRunAsync(repository, run, analysis.Model, cancellationToken);
        progress?.Report(new AnalysisProgress(AnalysisStage.Saving, 1, 1));
        return Summarize(repository, run, analysis.Model);
    }

    private static RepositoryImportSummaryDto Summarize(Repository repository, AnalysisRun run, ArchitectureModel model) => new()
    {
        RepositoryId = repository.Id.ToString(),
        Name = repository.Name,
        RootPath = repository.Location.LocalPath,
        AnalysisRunId = run.Id.ToString(),
        Status = run.Status == AnalysisRunStatus.Succeeded ? "succeeded" : "partially-succeeded",
        FileCount = model.Files.Count,
        ProjectCount = model.Projects.Count,
        RelationshipCount = model.Relationships.Count,
        WarningCount = run.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning),
        ErrorCount = run.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error),
        Duration = run.CompletedAt!.Value - run.StartedAt,
        Diagnostics = [.. run.Diagnostics
            .Where(d => d.Severity != DiagnosticSeverity.Info)
            .Select(d => new DiagnosticDto(
                d.Severity == DiagnosticSeverity.Error ? "error" : "warning",
                d.Code,
                d.Message,
                d.Location is { } location ? model.FindFile(location.FileId)?.Path.Value : null,
                d.Location?.StartLine))],
    };

    private static string NameOf(RepositoryRoot root) =>
        Path.GetFileName(root.FullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) is { Length: > 0 } name
            ? name
            : root.FullPath;
}
