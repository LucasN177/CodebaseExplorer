using CodebaseExplorer.Domain.Common;

namespace CodebaseExplorer.Application.AI;

/// <summary>
/// Persistent AI conversations (AI-006, DATABASE_SCHEMA.md §4.14–4.15). Conversations belong to a repository and are
/// deleted with it. Messages keep their validated citations and the manifest of the context sent.
/// </summary>
public interface IAiConversationStore
{
    /// <summary>Conversations of the repository, most recently updated first.</summary>
    Task<IReadOnlyList<AiConversation>> ListAsync(RepositoryId repositoryId, CancellationToken cancellationToken);

    Task<AiConversation?> FindAsync(Guid conversationId, CancellationToken cancellationToken);

    /// <summary>Messages in order.</summary>
    Task<IReadOnlyList<AiMessage>> LoadMessagesAsync(Guid conversationId, CancellationToken cancellationToken);

    /// <summary>
    /// Creates the conversation if it does not exist yet and appends the messages (their ordinals continue the
    /// conversation), setting its <see cref="AiConversation.UpdatedAt"/> — in one transaction.
    /// </summary>
    Task AppendAsync(AiConversation conversation, IReadOnlyList<AiMessage> messages, CancellationToken cancellationToken);

    /// <summary>Deletes the conversation with its messages; false if it did not exist.</summary>
    Task<bool> DeleteAsync(Guid conversationId, CancellationToken cancellationToken);
}

/// <summary>A conversation about one repository; <c>IsRemote</c>: context left this machine (cloud provider or non-loopback endpoint).</summary>
public sealed record AiConversation(
    Guid Id,
    RepositoryId RepositoryId,
    string Title,
    string ProviderId,
    string ModelId,
    bool IsRemote,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>A stored message; <see cref="Ordinal"/> is assigned by the store when appending.</summary>
public sealed record AiMessage(
    Guid Id,
    int Ordinal,
    AiRole Role,
    string Content,
    IReadOnlyList<AiCitation> Citations,
    AiContextManifest? Manifest,
    int? PromptTokens,
    int? CompletionTokens,
    DateTimeOffset CreatedAt);
