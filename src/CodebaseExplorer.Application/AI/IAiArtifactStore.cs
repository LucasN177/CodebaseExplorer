using CodebaseExplorer.Domain.Common;

namespace CodebaseExplorer.Application.AI;

/// <summary>
/// Reusable AI outputs attached to model elements (AI-008, DATABASE_SCHEMA.md §4.16), e.g. cached component
/// explanations. An artifact belongs to the analysis run it was generated for; a newer run makes it stale.
/// </summary>
public interface IAiArtifactStore
{
    /// <summary>The artifact of the element and kind generated for <paramref name="runId"/>, or null.</summary>
    Task<AiArtifact?> FindAsync(RepositoryId repositoryId, ElementId elementId, string artifactKind, AnalysisRunId runId,
        CancellationToken cancellationToken);

    /// <summary>Stores the artifact, replacing every earlier artifact of the same element and kind.</summary>
    Task SaveAsync(AiArtifact artifact, CancellationToken cancellationToken);
}

/// <summary>
/// A stored AI output for an element; <c>MessageId</c> points to the assistant message with its citations and context.
/// </summary>
public sealed record AiArtifact(
    Guid Id,
    RepositoryId RepositoryId,
    Guid? MessageId,
    ElementKind ElementKind,
    ElementId ElementId,
    string ArtifactKind,
    string Content,
    AnalysisRunId AnalysisRunId,
    DateTimeOffset CreatedAt)
{
    /// <summary>Explanation of a component (AI-008).</summary>
    public const string ComponentExplanation = "ComponentExplanation";
}
