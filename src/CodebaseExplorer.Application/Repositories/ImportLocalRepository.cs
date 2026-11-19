using System.Collections.Concurrent;
using CodebaseExplorer.Application.Analysis;
using CodebaseExplorer.Application.Common;
using CodebaseExplorer.Application.Git;
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
/// If the folder is inside a Git working tree (GIT-005, GIT_INTEGRATION.md §5), the run records HEAD and branch, and
/// the snapshot and per-file activity are stored with the model. Git problems never fail the import: they become
/// warnings of the run, and the model is saved without Git data.
/// </remarks>
public sealed class ImportLocalRepository(
    IRepositoryRootValidator rootValidator,
    IRepositoryAnalyzer analyzer,
    IArchitectureModelStore store,
    IGitRepositoryReader gitReader,
    IGitActivityStore gitStore,
    TimeProvider timeProvider,
    Settings.AnalysisSettingsStore? settingsStore = null)
{
    private readonly ConcurrentDictionary<string, byte> _running = new(RootPathComparer.Instance);

    public Task<Result<RepositoryImportSummaryDto>> ExecuteAsync(
        string path,
        IProgress<AnalysisProgress>? progress,
        CancellationToken cancellationToken) =>
        ExecuteAsync(path, location: null, progress, cancellationToken);

    /// <summary>
    /// Imports a folder; a repository registered for the first time gets <paramref name="location"/> (for app-managed
    /// clones, GIT-003) or <see cref="RepositoryLocation.Local"/>.
    /// </summary>
    internal async Task<Result<RepositoryImportSummaryDto>> ExecuteAsync(
        string path,
        Func<RepositoryRoot, RepositoryLocation>? location,
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
            return await ImportAsync(root, location, progress, cancellationToken);
        }
        finally
        {
            _running.TryRemove(root.FullPath, out _);
        }
    }

    private async Task<Result<RepositoryImportSummaryDto>> ImportAsync(
        RepositoryRoot root, Func<RepositoryRoot, RepositoryLocation>? location, IProgress<AnalysisProgress>? progress, CancellationToken cancellationToken)
    {
        var started = timeProvider.GetUtcNow();
        var settings = settingsStore is null ? Settings.AnalysisSettings.Default : await settingsStore.LoadAsync(cancellationToken); // limits, excludes and Git window of the Settings page (UI-009)
        var previous = await store.FindRepositoryByRootAsync(root.FullPath, cancellationToken);
        var repository = previous
            ?? Repository.Register(NameOf(root), location?.Invoke(root) ?? RepositoryLocation.Local(root.FullPath), started);

        var git = await ReadGitSnapshotAsync(root, cancellationToken);
        var run = AnalysisRun.Create(
            repository.Id, previous is null ? AnalysisTrigger.Initial : AnalysisTrigger.Manual, started, git.Snapshot?.HeadCommitSha, git.Snapshot?.Branch);
        run.Start();

        Result<RepositoryAnalysis> result;
        GitFileActivityReport? activity = null;
        try
        {
            result = await analyzer.AnalyzeAsync(new AnalysisRequest(repository.Id, root, run.Id, settings.ToLimits()), progress, cancellationToken);
            if (result.IsSuccess && git.Repository is not null && git.Snapshot?.HeadCommitSha is not null)
            {
                progress?.Report(new AnalysisProgress(AnalysisStage.ReadingGitHistory, 0, 1));
                var read = await gitReader.ReadFileActivityAsync(git.Repository, settings.ToHistoryWindow(started), null, cancellationToken);
                if (read.TryGetValue(out var report, out var readError))
                {
                    activity = report;
                }
                else
                {
                    git.Diagnostics.Add(GitWarning(readError));
                }

                progress?.Report(new AnalysisProgress(AnalysisStage.ReadingGitHistory, 1, 1));
            }
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
        run.Report(git.Diagnostics);
        run.Complete(timeProvider.GetUtcNow(), analysis.Statistics);
        repository.MarkAnalyzed(run);

        progress?.Report(new AnalysisProgress(AnalysisStage.Saving, 0, 1));
        await store.SaveRunAsync(repository, run, analysis.Model, cancellationToken);
        if (git.Snapshot is not null && activity is not null)
        {
            await gitStore.SaveAsync(repository.Id, run.Id, git.Snapshot, activity, started, cancellationToken);
        }

        await store.MarkOpenedAsync(repository.Id, timeProvider.GetUtcNow(), cancellationToken);
        progress?.Report(new AnalysisProgress(AnalysisStage.Saving, 1, 1));
        return Summarize(repository, run, analysis.Model);
    }

    /// <summary>Detects the Git working tree and reads its snapshot; a folder without Git yields an empty context.</summary>
    private async Task<GitContext> ReadGitSnapshotAsync(RepositoryRoot root, CancellationToken cancellationToken)
    {
        var context = new GitContext();
        var opened = await gitReader.OpenAsync(root.FullPath, cancellationToken);
        if (!opened.TryGetValue(out var repository, out var openError))
        {
            if (openError.Kind != ErrorKind.NotFound)
            {
                context.Diagnostics.Add(GitWarning(openError));
            }

            return context;
        }

        var snapshot = await gitReader.ReadSnapshotAsync(repository, cancellationToken);
        if (!snapshot.TryGetValue(out var info, out var snapshotError))
        {
            context.Diagnostics.Add(GitWarning(snapshotError));
            return context;
        }

        context.Repository = repository;
        context.Snapshot = info;
        return context;
    }

    private static AnalysisDiagnostic GitWarning(ApplicationError error) =>
        new(DiagnosticSeverity.Warning, error.Code, $"Git data was not read: {error.Message}");

    private sealed class GitContext
    {
        public GitRepositoryInfo? Repository { get; set; }

        public GitSnapshotInfo? Snapshot { get; set; }

        public List<AnalysisDiagnostic> Diagnostics { get; } = [];
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
            .Select(d => GetRunHistory.ToDto(d, fileId => model.FindFile(fileId)?.Path.Value))],
    };

    private static string NameOf(RepositoryRoot root) =>
        Path.GetFileName(root.FullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) is { Length: > 0 } name
            ? name
            : root.FullPath;
}
