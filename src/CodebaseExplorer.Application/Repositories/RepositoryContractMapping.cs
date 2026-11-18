using CodebaseExplorer.Contracts.Repositories;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Runs;

namespace CodebaseExplorer.Application.Repositories;

/// <summary>Shared conversions between repository ids/run statuses and their Contracts representation.</summary>
internal static class RepositoryContractMapping
{
    /// <summary>Parses a repository id as used in DTOs (32 hex characters, not empty).</summary>
    public static bool TryParseRepositoryId(string? value, out RepositoryId repositoryId)
    {
        if (Guid.TryParseExact(value, "N", out var guid) && guid != Guid.Empty)
        {
            repositoryId = new RepositoryId(guid);
            return true;
        }

        repositoryId = default;
        return false;
    }

    public static string ToContract(AnalysisRunStatus status) => status switch
    {
        AnalysisRunStatus.Pending => RunStatuses.Pending,
        AnalysisRunStatus.Running => RunStatuses.Running,
        AnalysisRunStatus.Succeeded => RunStatuses.Succeeded,
        AnalysisRunStatus.PartiallySucceeded => RunStatuses.PartiallySucceeded,
        AnalysisRunStatus.Failed => RunStatuses.Failed,
        AnalysisRunStatus.Cancelled => RunStatuses.Cancelled,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };
}
