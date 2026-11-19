namespace CodebaseExplorer.Contracts.AI;

/// <summary>
/// A stored AI conversation about a repository (AI-006). <c>Id</c> is in <c>N</c> format; <c>IsRemote</c> is true when
/// the context left this machine (remote endpoint or cloud provider).
/// </summary>
public sealed record AiConversationDto(
    string Id,
    string Title,
    string ProviderId,
    string ModelId,
    bool IsRemote,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// A message of a conversation. <c>Role</c> is <see cref="UserRole"/> or <see cref="AssistantRole"/>; assistant
/// <c>Content</c> is untrusted model output (render as text, never as HTML).
/// </summary>
public sealed record AiMessageDto(
    string Id,
    int Ordinal,
    string Role,
    string Content,
    IReadOnlyList<AiCitationDto> Citations,
    IReadOnlyList<AiContextEntryDto> Context,
    int? PromptTokens,
    int? CompletionTokens,
    DateTimeOffset CreatedAt)
{
    public const string UserRole = "user";

    public const string AssistantRole = "assistant";
}

/// <summary>A validated citation of an answer.</summary>
public sealed record AiCitationDto(string Text, string? Key, string? Path, int? StartLine, int? EndLine, string? ElementId, bool Verified, string? Problem);

/// <summary>One fact or snippet sent to the model as context (the context manifest); <c>Kind</c> is <c>fact</c> or <c>snippet</c>.</summary>
public sealed record AiContextEntryDto(string Key, string Kind, string Label, string? Path, int? StartLine, int? EndLine, string? ElementId);

/// <summary>The result of asking: the conversation (created on the first question) and the two new messages.</summary>
public sealed record AiExchangeDto(AiConversationDto Conversation, AiMessageDto Question, AiMessageDto Answer);
