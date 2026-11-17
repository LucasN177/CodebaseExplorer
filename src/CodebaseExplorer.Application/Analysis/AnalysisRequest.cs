using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Domain.Common;

namespace CodebaseExplorer.Application.Analysis;

/// <summary>Input of one analysis: which repository, where it lies, which run, which limits.</summary>
public sealed record AnalysisRequest
{
    public AnalysisRequest(RepositoryId repositoryId, RepositoryRoot root, AnalysisRunId analysisRunId, AnalysisLimits? limits = null)
    {
        if (repositoryId.Value == Guid.Empty)
        {
            throw new ArgumentException("An analysis request requires a repository id.", nameof(repositoryId));
        }

        if (analysisRunId.Value == Guid.Empty)
        {
            throw new ArgumentException("An analysis request requires an analysis run id.", nameof(analysisRunId));
        }

        RepositoryId = repositoryId;
        Root = root ?? throw new ArgumentNullException(nameof(root));
        AnalysisRunId = analysisRunId;
        Limits = limits ?? AnalysisLimits.Default;
    }

    public RepositoryId RepositoryId { get; }

    public RepositoryRoot Root { get; }

    public AnalysisRunId AnalysisRunId { get; }

    public AnalysisLimits Limits { get; }
}
