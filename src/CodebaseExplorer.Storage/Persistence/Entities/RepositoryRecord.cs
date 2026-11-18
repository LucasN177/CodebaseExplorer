using CodebaseExplorer.Domain.Repositories;

namespace CodebaseExplorer.Storage.Persistence.Entities;

/// <summary>Table <c>Repositories</c> (§4.1).</summary>
internal sealed class RepositoryRecord
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string LocalPath { get; set; } = string.Empty;

    public string? RemoteUrl { get; set; }

    public RepositorySourceKind SourceKind { get; set; }

    public DateTimeOffset AddedAt { get; set; }

    /// <summary>For "recent repositories" (REPO-005); not part of the Domain repository.</summary>
    public DateTimeOffset? LastOpenedAt { get; set; }

    public Guid? LastAnalysisRunId { get; set; }
}
