using CodebaseExplorer.Application.Common;
using CodebaseExplorer.Contracts.Repositories;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Runs;

namespace CodebaseExplorer.Application.Repositories;

/// <summary>
/// Use case: the analysis runs of a repository with their diagnostics, newest first (UI-005, WF-10).
/// </summary>
/// <remarks>
/// Diagnostic locations reference files by id. Paths are resolved against the current model; a file that
/// only existed in an older run's model (replaced since) has no path any more, only the line.
/// </remarks>
public sealed class GetRunHistory(IArchitectureModelStore store)
{
    public async Task<Result<IReadOnlyList<AnalysisRunDto>>> ExecuteAsync(string repositoryId, CancellationToken cancellationToken)
    {
        if (!RepositoryContractMapping.TryParseRepositoryId(repositoryId, out var id))
        {
            return ApplicationError.Validation(RepositoryErrorCodes.IdInvalid, "The repository id is not a valid id.");
        }

        if (await store.FindRepositoryAsync(id, cancellationToken) is not { } repository)
        {
            return ApplicationError.NotFound(RepositoryErrorCodes.NotFound, "The repository is no longer stored.");
        }

        var runs = await store.ListRunsAsync(id, cancellationToken);
        var current = await store.LoadCurrentModelAsync(id, cancellationToken);
        string? PathOf(ElementId fileId) => current?.Model.FindFile(fileId)?.Path.Value;

        IReadOnlyList<AnalysisRunDto> history = [.. runs.Select(run => new AnalysisRunDto
        {
            RunId = run.Id.ToString(),
            Status = RepositoryContractMapping.ToContract(run.Status),
            Trigger = run.Trigger switch
            {
                AnalysisTrigger.Initial => "initial",
                AnalysisTrigger.Manual => "manual",
                _ => "incremental",
            },
            StartedAt = run.StartedAt,
            CompletedAt = run.CompletedAt,
            Duration = run.CompletedAt - run.StartedAt,
            CommitSha = run.CommitSha,
            Branch = run.Branch,
            IsCurrent = repository.LastAnalysisRunId == run.Id,
            FilesScanned = run.Statistics.FilesScanned,
            ProjectCount = run.Statistics.Projects,
            RelationshipCount = run.Statistics.Relationships,
            ErrorCount = run.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error),
            WarningCount = run.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning),
            InfoCount = run.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Info),
            Diagnostics = [.. run.Diagnostics.Select(d => ToDto(d, PathOf))],
        })];
        return Result<IReadOnlyList<AnalysisRunDto>>.Success(history);
    }

    internal static DiagnosticDto ToDto(AnalysisDiagnostic diagnostic, Func<ElementId, string?> pathOf) => new(
        diagnostic.Severity switch
        {
            DiagnosticSeverity.Error => "error",
            DiagnosticSeverity.Warning => "warning",
            _ => "info",
        },
        diagnostic.Code,
        diagnostic.Message,
        diagnostic.Location is { } location ? pathOf(location.FileId) : null,
        diagnostic.Location?.StartLine,
        diagnostic.Analyzer?.Value);
}
