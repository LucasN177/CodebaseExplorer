using CodebaseExplorer.Application.Common;
using CodebaseExplorer.Contracts.Repositories;
using CodebaseExplorer.Domain.Projects;
using CodebaseExplorer.Domain.Runs;

namespace CodebaseExplorer.Application.Repositories;

/// <summary>
/// Use case: the overview of an analyzed repository (UI-004, WF-01 step 5) — languages, projects by
/// ecosystem, external dependencies, the run of the current model and its warnings/errors — computed from
/// the stored model.
/// </summary>
public sealed class GetRepositoryOverview(IArchitectureModelStore store)
{
    public async Task<Result<RepositoryOverviewDto>> ExecuteAsync(string repositoryId, CancellationToken cancellationToken)
    {
        if (!RepositoryContractMapping.TryParseRepositoryId(repositoryId, out var id))
        {
            return ApplicationError.Validation(RepositoryErrorCodes.IdInvalid, "The repository id is not a valid id.");
        }

        if (await store.LoadCurrentModelAsync(id, cancellationToken) is not { } stored)
        {
            return ApplicationError.NotFound(RepositoryErrorCodes.NotAnalyzed, "The repository has not been analyzed yet.");
        }

        var model = stored.Model;
        var run = stored.Run;
        var latest = (await store.ListRunsAsync(id, cancellationToken)).FirstOrDefault();

        return new RepositoryOverviewDto
        {
            RepositoryId = stored.Repository.Id.ToString(),
            Name = stored.Repository.Name,
            RootPath = stored.Repository.Location.LocalPath,
            FileCount = model.Files.Count,
            Languages = [.. model.Files
                .GroupBy(f => f.Language.Value, StringComparer.Ordinal)
                .Select(g => new LanguageStatisticsDto(g.Key, g.Count(), g.Sum(f => (long)(f.LineCount ?? 0))))
                .OrderByDescending(l => l.FileCount)
                .ThenBy(l => l.Language, StringComparer.Ordinal)],
            ProjectsByEcosystem = [.. model.Projects
                .Where(p => !p.IsPlaceholder())
                .GroupBy(p => p.Ecosystem)
                .Select(g => new EcosystemStatisticsDto(EcosystemName(g.Key), g.Count()))
                .OrderByDescending(e => e.ProjectCount)
                .ThenBy(e => e.Ecosystem, StringComparer.Ordinal)],
            MissingProjectCount = model.Projects.Count(p => p.IsPlaceholder()),
            ExternalDependencyCount = model.ExternalDependencies.Count,
            RelationshipCount = model.Relationships.Count,
            ModelRun = Info(run),
            NewerRun = latest is not null && latest.Id != run.Id && latest.StartedAt > run.StartedAt ? Info(latest) : null,
            WarningCount = run.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning),
            ErrorCount = run.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error),
        };
    }

    private static RunInfoDto Info(AnalysisRun run) =>
        new(run.Id.ToString(), RepositoryContractMapping.ToContract(run.Status), run.StartedAt, run.CompletedAt);

    private static string EcosystemName(Ecosystem ecosystem) => ecosystem switch
    {
        Ecosystem.DotNet => "dotnet",
        Ecosystem.Node => "node",
        Ecosystem.Python => "python",
        _ => "other",
    };
}
